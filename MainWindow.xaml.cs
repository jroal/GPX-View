using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace GpxViewer
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GpxItem> _visibleGpxFiles = new ObservableCollection<GpxItem>();
        private readonly List<GpxItem> _allGpxFiles = new List<GpxItem>();
        private bool _isMapReady;
        private bool _suppressSelectionChanged;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly string[] RoutePalette =
        {
            "#e41a1c", "#377eb8", "#4daf4a", "#984ea3", "#ff7f00",
            "#ffff33", "#a65628", "#f781bf", "#1f78b4", "#33a02c"
        };

        public MainWindow()
        {
            InitializeComponent();
            GpxFileListBox.ItemsSource = _visibleGpxFiles;
            InitMap();
        }

        private async void InitMap()
        {
            try
            {
                var webViewDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GPX View",
                    "WebView2");

                Directory.CreateDirectory(webViewDataRoot);

                var primaryEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewDataRoot);
                try
                {
                    await MapWebView.EnsureCoreWebView2Async(primaryEnvironment);
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x800700AA))
                {
                    var recoveryFolder = Path.Combine(webViewDataRoot, $"recovery-{DateTime.UtcNow:yyyyMMddHHmmss}");
                    Directory.CreateDirectory(recoveryFolder);

                    var recoveryEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: recoveryFolder);
                    await MapWebView.EnsureCoreWebView2Async(recoveryEnvironment);
                }

                ConfigureTileRequestHeaders();
                MapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                MapWebView.NavigationCompleted += MapWebView_NavigationCompleted;

            string html = @"
            <!DOCTYPE html>
            <html>
            <head>
                <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'/>
                <link rel='stylesheet' href='https://unpkg.com/leaflet-draw@1.0.4/dist/leaflet.draw.css'/>
                <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
                <script src='https://unpkg.com/leaflet-draw@1.0.4/dist/leaflet.draw.js'></script>
                <style>#map { height: 100vh; margin: 0; }</style>
            </head>
            <body>
                <div id='map'></div>
                <script>
                    var map = L.map('map').setView([0, 0], 2);

                    var defaultLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                        maxZoom: 19,
                        attribution: '© OpenStreetMap contributors'
                    });

                    var satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
                        maxZoom: 19,
                        attribution: 'Tiles © Esri'
                    });

                    defaultLayer.addTo(map);

                    var baseLayers = {
                        'Default': defaultLayer,
                        'Satellite': satelliteLayer
                    };

                    L.control.layers(baseLayers, null, { collapsed: false }).addTo(map);

                    var routeLayers = {};
                    var selectionLayer = new L.FeatureGroup().addTo(map);
                    var mapWindowDebounceTimer = null;

                    var drawControl = new L.Control.Draw({
                        edit: {
                            featureGroup: selectionLayer,
                            edit: false,
                            remove: false
                        },
                        draw: {
                            polyline: false,
                            polygon: false,
                            circle: false,
                            marker: false,
                            circlemarker: false,
                            rectangle: true
                        }
                    });

                    map.addControl(drawControl);

                    map.on(L.Draw.Event.CREATED, function (e) {
                        selectionLayer.clearLayers();
                        selectionLayer.addLayer(e.layer);
                        var bounds = e.layer.getBounds();
                        window.chrome.webview.postMessage({
                            type: 'areaSelected',
                            source: 'drawSelection',
                            south: bounds.getSouth(),
                            west: bounds.getWest(),
                            north: bounds.getNorth(),
                            east: bounds.getEast()
                        });
                    });

                    function postMapWindowBounds() {
                        var bounds = map.getBounds();
                        window.chrome.webview.postMessage({
                            type: 'areaSelected',
                            source: 'mapWindow',
                            south: bounds.getSouth(),
                            west: bounds.getWest(),
                            north: bounds.getNorth(),
                            east: bounds.getEast()
                        });
                    }

                    function scheduleMapWindowBoundsPost() {
                        if (mapWindowDebounceTimer) {
                            clearTimeout(mapWindowDebounceTimer);
                        }

                        mapWindowDebounceTimer = setTimeout(function () {
                            postMapWindowBounds();
                        }, 2000);
                    }

                    map.on('moveend', function () {
                        scheduleMapWindowBoundsPost();
                    });

                    function clearRoutes() {
                        for (var id in routeLayers) {
                            if (Object.prototype.hasOwnProperty.call(routeLayers, id)) {
                                map.removeLayer(routeLayers[id]);
                            }
                        }
                        routeLayers = {};
                    }

                    function setRoutes(routes) {
                        clearRoutes();

                        if (!Array.isArray(routes)) {
                            return;
                        }

                        var routeGroup = [];
                        for (var i = 0; i < routes.length; i++) {
                            var route = routes[i];
                            if (!route.coords || route.coords.length === 0) {
                                continue;
                            }

                            var layer = L.polyline(route.coords, {
                                color: route.color,
                                weight: 4,
                                opacity: 0.85
                            }).addTo(map);

                            layer.routeId = route.id;
                            layer.on('click', function (ev) {
                                var routeId = ev.target.routeId;
                                highlightRoute(routeId);
                                window.chrome.webview.postMessage({ type: 'routeClicked', id: routeId });
                            });

                            routeLayers[route.id] = layer;
                            routeGroup.push(layer);
                        }

                        // Keep the current user viewport; do not auto-fit bounds on redraw.
                        if (routeGroup.length === 0) {
                            scheduleMapWindowBoundsPost();
                        }
                    }

                    function highlightRoute(routeId) {
                        for (var id in routeLayers) {
                            if (Object.prototype.hasOwnProperty.call(routeLayers, id)) {
                                routeLayers[id].setStyle({ weight: 4, opacity: 0.45 });
                            }
                        }

                        if (routeLayers[routeId]) {
                            routeLayers[routeId].setStyle({ weight: 7, opacity: 1.0 });
                            routeLayers[routeId].bringToFront();
                        }
                    }
                </script>
            </body>
            </html>";

                MapWebView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Map initialization failed.\n\n{ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        private void MapWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _isMapReady = e.IsSuccess;
            if (_isMapReady)
            {
                RenderRoutes(_visibleGpxFiles);
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var messageType = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(messageType))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (messageType.Equals("areaSelected", StringComparison.OrdinalIgnoreCase))
                {
                    HandleAreaSelection(root);
                }
                else if (messageType.Equals("routeClicked", StringComparison.OrdinalIgnoreCase) &&
                         root.TryGetProperty("id", out var routeIdElement))
                {
                    SelectRouteFromMap(routeIdElement.GetString());
                }
            });
        }

        private static string GetRouteColor(int index)
        {
            return RoutePalette[index % RoutePalette.Length];
        }

        private void UpdateVisibleFiles(IEnumerable<GpxItem> files)
        {
            _visibleGpxFiles.Clear();
            foreach (var file in files)
            {
                _visibleGpxFiles.Add(file);
            }
        }

        private void HandleAreaSelection(JsonElement root)
        {
            var source = root.TryGetProperty("source", out var sourceElement)
                ? sourceElement.GetString()
                : null;

            if (HoldFileListCheckBox.IsChecked == true &&
                string.Equals(source, "mapWindow", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!root.TryGetProperty("south", out var southElement) ||
                !root.TryGetProperty("west", out var westElement) ||
                !root.TryGetProperty("north", out var northElement) ||
                !root.TryGetProperty("east", out var eastElement))
            {
                return;
            }

            var south = southElement.GetDouble();
            var west = westElement.GetDouble();
            var north = northElement.GetDouble();
            var east = eastElement.GetDouble();

            var visible = _allGpxFiles
                .Where(item => item.Points != null && item.Points.Length > 0)
                .Where(item => item.Points.All(p =>
                    p != null &&
                    p.Length >= 2 &&
                    p[0] >= south && p[0] <= north &&
                    p[1] >= west && p[1] <= east))
                .ToList();

            UpdateVisibleFiles(visible);
            RenderRoutes(visible);
        }

        private void SelectRouteFromMap(string? routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                return;
            }

            var match = _visibleGpxFiles.FirstOrDefault(file => file.Id == routeId);
            if (match is null)
            {
                return;
            }

            _suppressSelectionChanged = true;
            GpxFileListBox.SelectedItem = match;
            GpxFileListBox.ScrollIntoView(match);
            _suppressSelectionChanged = false;
        }

        private void RenderRoutes(IEnumerable<GpxItem> routes)
        {
            if (!_isMapReady || MapWebView.CoreWebView2 is null)
            {
                return;
            }

            var payload = routes.Select(route => new MapRouteDto
            {
                Id = route.Id,
                Name = route.FileName,
                Color = route.RouteColor,
                Coords = route.Points
            }).ToArray();

            var routesJson = JsonSerializer.Serialize(payload, _jsonOptions);
            _ = MapWebView.CoreWebView2.ExecuteScriptAsync($"setRoutes({routesJson});");

            if (GpxFileListBox.SelectedItem is GpxItem selected)
            {
                var selectedIdJson = JsonSerializer.Serialize(selected.Id, _jsonOptions);
                _ = MapWebView.CoreWebView2.ExecuteScriptAsync($"highlightRoute({selectedIdJson});");
            }
        }

        private void ConfigureTileRequestHeaders()
        {
            var core = MapWebView.CoreWebView2;
            core.Settings.UserAgent = "GPX-View/1.0 (+desktop-webview2)";
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                _allGpxFiles.Clear();
                var gpxFiles = Directory.GetFiles(dialog.FolderName, "*.gpx");

                var colorIndex = 0;
                foreach (var file in gpxFiles)
                {
                    double[][] points;
                    try
                    {
                        points = ParseGpxPoints(file);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping invalid GPX '{file}': {ex.Message}");
                        continue;
                    }

                    if (points.Length == 0)
                    {
                        continue;
                    }

                    var color = GetRouteColor(colorIndex);
                    _allGpxFiles.Add(new GpxItem
                    {
                        Id = $"route-{colorIndex}",
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Points = points,
                        RouteColor = color,
                        RouteBrush = new BrushConverter().ConvertFromString(color) as Brush ?? Brushes.Black
                    });

                    colorIndex++;
                }

                UpdateVisibleFiles(_allGpxFiles);
                RenderRoutes(_allGpxFiles);
            }
        }

        private void GpxFileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged)
            {
                return;
            }

            if (GpxFileListBox.SelectedItem is GpxItem selected && _isMapReady && MapWebView.CoreWebView2 is not null)
            {
                var selectedIdJson = JsonSerializer.Serialize(selected.Id, _jsonOptions);
                _ = MapWebView.CoreWebView2.ExecuteScriptAsync($"highlightRoute({selectedIdJson});");
            }
        }

        private void GpxFileListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void ViewTotalMiles_Click(object sender, RoutedEventArgs e)
        {
            if (GpxFileListBox.SelectedItem is not GpxItem selected)
            {
                MessageBox.Show(this, "Select a GPX file first.", "Total Miles", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var miles = CalculateTotalMiles(selected.Points);
            MessageBox.Show(
                this,
                $"{selected.FileName}\nTotal distance: {miles:F2} miles",
                "Total Miles",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static double CalculateTotalMiles(double[][] points)
        {
            if (points is null || points.Length < 2)
            {
                return 0;
            }

            var totalMiles = 0d;
            for (var i = 1; i < points.Length; i++)
            {
                var previous = points[i - 1];
                var current = points[i];

                if (previous is null || current is null || previous.Length < 2 || current.Length < 2)
                {
                    continue;
                }

                totalMiles += CalculateGreatCircleMiles(previous[0], previous[1], current[0], current[1]);
            }

            return totalMiles;
        }

        private static double CalculateGreatCircleMiles(double lat1, double lon1, double lat2, double lon2)
        {
            const double EarthRadiusMiles = 3958.7613;

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var lat1Rad = ToRadians(lat1);
            var lat2Rad = ToRadians(lat2);

            var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                    Math.Cos(lat1Rad) * Math.Cos(lat2Rad) * Math.Pow(Math.Sin(dLon / 2), 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMiles * c;
        }

        private static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private double[][] ParseGpxPoints(string filePath)
        {
            var doc = XDocument.Load(filePath);

            return doc.Descendants()
                      .Where(pt =>
                          pt.Name.LocalName == "trkpt" ||
                          pt.Name.LocalName == "rtept" ||
                          pt.Name.LocalName == "wpt")
                      .Select(pt =>
                      {
                          var latText = pt.Attribute("lat")?.Value;
                          var lonText = pt.Attribute("lon")?.Value;

                          if (double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                              double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                          {
                              return new double[] { lat, lon };
                          }

                          return null;
                      })
                      .Where(p => p is not null)
                      .Select(p => p!)
                      .ToArray();
        }
    }

    public class GpxItem
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public double[][] Points { get; set; } = [];
        public string RouteColor { get; set; } = "#000000";
        public Brush RouteBrush { get; set; } = Brushes.Black;
    }

    public class MapRouteDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#000000";
        public double[][] Coords { get; set; } = [];
    }
}