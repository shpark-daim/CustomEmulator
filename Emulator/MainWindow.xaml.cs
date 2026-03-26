using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Emulator.Controls;
using Emulator.ViewModels;
using Microsoft.Win32;

namespace Emulator;

public partial class MainWindow : Window
{
    private readonly EmulatorViewModel _vm = new();
    private string _fullConfigPath = "";
    private LocalSettings _settings = LocalSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        Title = $"Emulator v{version}";
        ApplySettings();
        _vm.ConfigureSeq("http://localhost:5341");
    }

    // ── Config 열기 ────────────────────────────────────────────────────────────
    private void OpenConfig_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        OpenConfigFile(dlg.FileName);
    }

    private void OpenConfigFile(string path)
    {
        _fullConfigPath = path;
        ConfigPathBtn.Tag     = Path.GetFileName(path);
        ConfigPathBtn.ToolTip = path;
        ConfigPathBtn.Visibility = Visibility.Visible;

        ApplyRestPort();
        _vm.LoadConfig(path);
        RebuildCanvas();
    }

    // ── 파일경로 클릭 → 클립보드 복사 ────────────────────────────────────────
    private void ConfigPath_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_fullConfigPath)) return;

        Clipboard.SetText(_fullConfigPath);
        var btn = (Button)s;
        var prev = btn.Tag;
        btn.Tag = "✓ 복사됨";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { btn.Tag = prev; timer.Stop(); };
        timer.Start();
    }

    // ── 전체 연결 ──────────────────────────────────────────────────────────────
    private async void ConnectAll_Click(object s, RoutedEventArgs e)
    {
        ApplyRestPort();
        try { await _vm.ConnectAllAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Connection Error"); }
    }

    // ── 전체 해제 ──────────────────────────────────────────────────────────────
    private async void DisconnectAll_Click(object s, RoutedEventArgs e)
    {
        try { await _vm.DisconnectAllAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Disconnect Error"); }
    }

    // ── 캔버스 재구성 ──────────────────────────────────────────────────────────
    private void RebuildCanvas()
    {
        ObjectCanvas.Children.Clear();

        foreach (var objVm in _vm.Objects)
        {
            var card = new ObjectCard { DataContext = objVm };

            card.ConnectToggleRequested += async vm =>
            {
                ApplyRestPort();
                try
                {
                    if (vm.IsConnected) await vm.DisconnectAsync();
                    else                await _vm.ConnectObjectAsync(vm);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Connection Error"); }
            };

            // 에디터에서 저장된 위치 사용 (상대적 위치 보존)
            Canvas.SetLeft(card, objVm.CanvasX);
            Canvas.SetTop(card, objVm.CanvasY);
            ObjectCanvas.Children.Add(card);
        }

        // 레이아웃 완료 후 겹침 해소 (실제 렌더 크기 기준)
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ResolveOverlaps));
    }

    // ── 겹침 해소 ──────────────────────────────────────────────────────────────
    /// <summary>
    /// 에디터 카드(작음)와 에뮬레이터 카드(큼)의 크기 차이로 겹치는 카드를
    /// 실제 렌더링된 크기 기준으로 자동 정렬합니다.
    /// 에디터 위치의 상대적 순서(위→아래, 왼→오른쪽)는 유지합니다.
    /// </summary>
    private void ResolveOverlaps()
    {
        const double gap = 20;  // 카드 사이 최소 여백

        var cards = ObjectCanvas.Children.OfType<ObjectCard>().ToList();
        if (cards.Count == 0) return;

        // 에디터 저장 위치 기준으로 정렬 (위→아래, 동일 행이면 왼→오른쪽)
        cards.Sort((a, b) =>
        {
            var rowDiff = Canvas.GetTop(a).CompareTo(Canvas.GetTop(b));
            return Math.Abs(rowDiff) > 40 ? rowDiff   // 40px 이상 차이면 다른 행
                                          : Canvas.GetLeft(a).CompareTo(Canvas.GetLeft(b));
        });

        var placed = new List<Rect>();

        foreach (var card in cards)
        {
            // 실제 렌더링된 크기 사용 (Loaded 이후이므로 확정값)
            var w = card.ActualWidth  > 0 ? card.ActualWidth  : 240;
            var h = card.ActualHeight > 0 ? card.ActualHeight : 200;

            var x = Canvas.GetLeft(card);
            var y = Canvas.GetTop(card);

            // 겹치지 않을 때까지 오른쪽으로 이동
            for (int retry = 0; retry < 200; retry++)
            {
                var proposed = new Rect(x, y, w, h);
                bool overlaps = placed.Any(r =>
                    r.IntersectsWith(new Rect(x - gap / 2, y - gap / 2,
                                              w + gap, h + gap)));
                if (!overlaps) break;

                // 겹치는 카드의 오른쪽 끝 + gap으로 이동
                var blocker = placed.First(r =>
                    r.IntersectsWith(new Rect(x - gap / 2, y - gap / 2,
                                              w + gap, h + gap)));
                x = blocker.Right + gap;
            }

            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            placed.Add(new Rect(x, y, w, h));
        }
    }

    private void ApplyRestPort() { }   // REST 포트는 settings에서 관리

    // ── 설정 ───────────────────────────────────────────────────────────────────
    private void Settings_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _settings = dlg.Result;
        _settings.Save();
        ApplySettings();
    }

    private void ApplySettings()
    {
        _vm.RestPort               = _settings.RestPort;
        _vm.MqttBrokerHostOverride = _settings.MqttBrokerHost;
        _vm.MqttBrokerPortOverride = _settings.MqttBrokerPort;
    }
}
