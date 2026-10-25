using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Filters;
using System.Reflection;
using BtcAdvisor.Models;
using BtcAdvisor.Repository;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Worker;

namespace BtcAdvisor;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        // Konfigurationen laden
        services.Configure<HostOptions>(opts =>
            opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>();
            services.AddMemoryCache();
        services.Configure<ApiSettings>(Configuration.GetSection("ApiSettings"));
        services.Configure<RiskControlOptions>(Configuration.GetSection("RiskControls"));
        services.AddHttpClient("MARKETDATA_APP", client =>
        {
            client.BaseAddress = new Uri("https://api.marketdata.app/");
        });

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"));
            options.EnableDetailedErrors(true);
            options.EnableSensitiveDataLogging(true);
        });
        services.AddHttpClient<TradeAdviceService>(client =>
        {
            client.BaseAddress = new Uri("http://192.168.178.78:6003/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddScoped(sp =>
        {
            // Server-side Blazor calls its own API via this HttpClient. When the app is reached
            // through a reverse proxy (e.g. Caddy at bitcoin.marclohrer.de), NavigationManager.BaseUri
            // points to the public HTTPS URL, which is not reachable / trusted from inside the
            // container. Use a loopback base address for self-calls; override via "SelfBaseUrl".
            var selfBaseUrl = Configuration["SelfBaseUrl"];
            if (string.IsNullOrWhiteSpace(selfBaseUrl))
            {
                var httpPort = Environment.GetEnvironmentVariable("HttpPort");
                if (string.IsNullOrWhiteSpace(httpPort))
                {
                    httpPort = "6003";
                }
                selfBaseUrl = $"http://localhost:{httpPort}/";
            }
            if (!selfBaseUrl.EndsWith('/'))
            {
                selfBaseUrl += "/";
            }
            return new HttpClient
            {
                BaseAddress = new Uri(selfBaseUrl)
            };
        });

        services.AddControllers().AddNewtonsoftJson(x => x.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<IBitcoinRepository, BitcoinRepository>();
        services.AddScoped<ISellActionLogRepository, SellActionLogRepository>();
        services.AddScoped<IActionLogRepository, ActionLogRepository>();
        services.AddScoped<IBuyActionLogRepository, BuyActionLogRepository>();
        services.AddScoped<ITradingRepository, TradingRepository>();
        services.AddScoped<IShortSellerStateRepository, ShortSellerStateRepository>();
        services.AddScoped<IStockCandleRepository, StockCandleRepository>();

        services.AddTransient<IActionLogService, ActionLogService>();
        services.AddScoped<IMarketDataApiWrapper, MarketDataApiWrapper>();
        services.AddTransient<IShortSellerDetectorService, ShortSellerDetectorService>();
        services.AddTransient<IShortSellerAlertService, ShortSellerAlertService>();
        services.AddTransient<IStockCandlePersistingService, StockCandlePersistingService>();
        services.AddTransient<IHistoricalBinanceCandleService, HistoricalBinanceCandleService>();
        services.AddTransient<IGapDetectionService, GapDetectionService>();
        services.AddTransient<ICandleBackfillService, CandleBackfillService>();
        services.AddTransient<IIndicatorBackfillService, IndicatorBackfillService>();
        services.AddTransient<IGapBackfillCoordinator, GapBackfillCoordinator>();
        services.AddSingleton<GapCheckerStatusStore>();
        services.Configure<GapCheckerOptions>(Configuration.GetSection(GapCheckerOptions.SectionName));

        services.AddSingleton<IBinanceKlineService, BinanceKlineService>();
        services.AddTransient<IBitcoinIndicatorService, BinanceLocalIndicatorService>();
        services.AddTransient<IBitcoinStatisticsService, BitcoinStatisticsService>();
        services.AddTransient<ILocalIndicatorComputationService, LocalIndicatorComputationService>();
        services.AddSingleton<IIndicatorRegistry, IndicatorRegistry>();
        services.AddTransient<IBitcoinTradeAdvisor, BitcoinTradeAdvisor>();
        services.AddTransient<IMarketRegimeService, MarketRegimeService>();
        services.AddTransient<ITradeOpportunityService, TradeOpportunityService>();
        services.AddTransient<IBacktestService, BacktestService>();
        services.AddTransient<IMonteCarloSignificanceService, MonteCarloSignificanceService>();
        services.AddTransient<IRegimeStabilityService, RegimeStabilityService>();
        services.AddTransient<IStationaryBootstrapService, StationaryBootstrapService>();
        services.AddTransient<IBacktestRiskControlService, BacktestRiskControlService>();
        services.AddTransient<IAtrLookupService, AtrLookupService>();
        services.AddTransient<IMarketContextService, MarketContextService>();
        services.AddTransient<IBearMarketFilterService, BearMarketFilterService>();
        services.Configure<VolumeVolatilityOptions>(Configuration.GetSection("VolumeVolatility"));
        services.Configure<BearMarketFilterOptions>(Configuration.GetSection("BearMarketFilter"));

        services.AddHostedService<BinanceCandleWarmupService>();
        services.AddHostedService<StockPriceFetcher>();
        services.AddHostedService<GapCheckerBackgroundService>();
        services.AddHostedService<ShortSellerWatcherService>();
        services.AddHostedService<BitcoinAdviceBackgroundService>();

        services.Configure<TelegramBotOptions>(options =>
        {
            Configuration.GetSection("TelegramBotOptions").Bind(options);
            var chatIdsAsString = Configuration["TelegramBotOptions:ChatIds"];
            if (!string.IsNullOrEmpty(chatIdsAsString))
            {
                var ids = (chatIdsAsString ?? "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList()
                    .AsReadOnly();
                options.ChatIds = ids;
            }
        });
        services.AddSingleton<ITelegramBotService, TelegramBotService>();
        services.AddScoped<BitcoinAdviceChangeService>();

        // Register Swagger generator
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "My Bitcoin API", Version = "v1" });
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            c.IncludeXmlComments(xmlPath);
            c.ExampleFilters();
        });
        services.AddSwaggerExamplesFromAssemblyOf<Program>();
        services.AddMvcCore().AddApiExplorer();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ApplicationDbContext dbContext)
    {
        // Führe ausstehende Migrationen beim Starten aus
        try
        {
            if (dbContext.Database.GetPendingMigrations().Any())
            {
                dbContext.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            // Logging hinzufügen, wenn nötig
            System.Diagnostics.Debug.WriteLine($"Migrations error: {ex.Message}");
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseStaticFiles();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseRouting();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
            c.RoutePrefix = "swagger";
            if (!env.IsDevelopment())
            {
                c.DocumentTitle = "My API - HTTPS";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1 (HTTPS)");
            }
        });

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHealthChecks("/health");
            endpoints.MapControllers();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
        });
    }
}
