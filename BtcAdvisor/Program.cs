namespace BtcAdvisor;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(6003); // Single IPv6Any listen, avoids conflict
                });
                webBuilder.UseStartup<Startup>();
            });
}

