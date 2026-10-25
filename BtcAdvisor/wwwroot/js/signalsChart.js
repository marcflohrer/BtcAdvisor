window.signalsChart = (() => {
  let chart = null;
  let lineSeries = null;
  let resizeHandler = null;

  function getPriceScaleMode(modeName) {
    if (!window.LightweightCharts || !LightweightCharts.PriceScaleMode) {
      return 0;
    }

    if (modeName === "logarithmic") {
      return LightweightCharts.PriceScaleMode.Logarithmic;
    }

    return LightweightCharts.PriceScaleMode.Normal;
  }

  function createAreaSeries(chartApi, options) {
    if (!chartApi) {
      return null;
    }

    if (typeof chartApi.addAreaSeries === "function") {
      return chartApi.addAreaSeries(options);
    }

    if (typeof chartApi.addSeries === "function" && window.LightweightCharts && LightweightCharts.AreaSeries) {
      return chartApi.addSeries(LightweightCharts.AreaSeries, options);
    }

    if (typeof chartApi.addLineSeries === "function") {
      return chartApi.addLineSeries({
        lineColor: options.lineColor,
        lineWidth: options.lineWidth,
        priceLineVisible: options.priceLineVisible,
        crosshairMarkerVisible: options.crosshairMarkerVisible
      });
    }

    if (typeof chartApi.addSeries === "function" && window.LightweightCharts && LightweightCharts.LineSeries) {
      return chartApi.addSeries(LightweightCharts.LineSeries, {
        lineColor: options.lineColor,
        lineWidth: options.lineWidth,
        priceLineVisible: options.priceLineVisible,
        crosshairMarkerVisible: options.crosshairMarkerVisible
      });
    }

    return null;
  }

  function setSeriesMarkers(series, markers) {
    const safeMarkers = markers || [];

    if (!series) {
      return;
    }

    if (typeof series.setMarkers === "function") {
      series.setMarkers(safeMarkers);
      return;
    }

    if (window.LightweightCharts && typeof LightweightCharts.createSeriesMarkers === "function") {
      LightweightCharts.createSeriesMarkers(series, safeMarkers);
    }
  }

  function ensureChart(hostId) {
    const host = document.getElementById(hostId);
    if (!host) {
      return null;
    }

    if (!chart) {
      chart = LightweightCharts.createChart(host, {
        layout: {
          background: { color: "#0d1728" },
          textColor: "#a9bddb"
        },
        rightPriceScale: {
          borderColor: "#28415f"
        },
        timeScale: {
          borderColor: "#28415f",
          timeVisible: true,
          secondsVisible: false
        },
        grid: {
          vertLines: { color: "rgba(71, 93, 127, 0.28)" },
          horzLines: { color: "rgba(71, 93, 127, 0.28)" }
        },
        crosshair: {
          mode: LightweightCharts.CrosshairMode.Normal
        },
        localization: {
          priceFormatter: price => Number(price).toLocaleString("de-DE", { maximumFractionDigits: 2 })
        },
        width: host.clientWidth,
        height: host.clientHeight
      });

      lineSeries = createAreaSeries(chart, {
        lineColor: "#5da8ff",
        topColor: "rgba(93, 168, 255, 0.34)",
        bottomColor: "rgba(93, 168, 255, 0.04)",
        lineWidth: 2,
        priceLineVisible: true,
        crosshairMarkerVisible: true
      });

      resizeHandler = () => {
        if (chart && host) {
          chart.applyOptions({ width: host.clientWidth, height: host.clientHeight });
        }
      };
      window.addEventListener("resize", resizeHandler);
    }

    return host;
  }

  function render(hostId, seriesData, markerData, options) {
    const host = ensureChart(hostId);
    if (!host || !chart || !lineSeries) {
      return;
    }

    const priceScaleMode = getPriceScaleMode(options?.priceScaleMode);
    chart.applyOptions({
      rightPriceScale: {
        borderColor: "#28415f",
        mode: priceScaleMode
      }
    });

    lineSeries.setData(seriesData || []);
    setSeriesMarkers(lineSeries, markerData);
    chart.timeScale().fitContent();
  }

  return {
    render
  };
})();
