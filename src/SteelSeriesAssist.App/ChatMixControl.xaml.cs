using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SteelSeriesAssist.Domain;
using SteelSeriesAssist.Sonar;

namespace SteelSeriesAssist.App;

public partial class ChatMixControl : System.Windows.Controls.UserControl, IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private SonarClient? _client;
    private Uri? _address;
    private float? _pending;
    private bool _writing;
    private bool _reading;
    private bool _applying;
    private bool _disposed;
    private int _revision;

    public ChatMixControl()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    public void Connect(Uri address)
    {
        if (_disposed || _address == address) return;
        _revision++;
        _pending = null;
        _client?.Dispose();
        _client = new SonarClient(address);
        _address = address;
        SetEnabled(false);
        HintText.Text = "正在读取聊天混音…";
        Refresh();
    }

    public async void Refresh()
    {
        if (_disposed || _client is null || !IsVisible || _reading || _writing ||
            BalanceSlider.IsMouseCaptureWithin || Mouse.LeftButton == MouseButtonState.Pressed) return;
        _reading = true;
        var revision = _revision;
        var client = _client;
        try
        {
            var state = await client.GetChatMixAsync(_lifetime.Token);
            if (!_disposed && revision == _revision && !_writing && !BalanceSlider.IsMouseCaptureWithin)
                Apply(state);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception) when (!_disposed)
        {
            if (revision == _revision && !_writing)
            {
                SetEnabled(false);
                HintText.Text = "聊天混音暂不可用，连接恢复后自动重试";
            }
        }
        finally { _reading = false; }
    }

    private void Apply(ChatMixState state)
    {
        _applying = true;
        try { BalanceSlider.Value = Math.Round(state.Balance * 100); }
        finally { _applying = false; }
        UpdateLabel();
        SetEnabled(state.CanAdjust);
        HintText.Text = state.State switch
        {
            "enabled" => "向左偏游戏 · 向右偏聊天 · 点击右上角恢复均衡",
            "finiteWheel" => "由耳机旋钮控制，请使用硬件调节聊天混音",
            "differentDeviceSelected" => "请在 GG 中为游戏和聊天选择同一输出设备",
            "noDeviceSelected" => "请先在 GG 中选择游戏和聊天的输出设备",
            _ => "当前 Sonar 状态不支持调整聊天混音"
        };
    }

    private void SetEnabled(bool enabled)
    {
        BalanceSlider.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
    }

    private void UpdateLabel()
    {
        var value = (int)Math.Round(BalanceSlider.Value);
        ResetButton.Content = value == 0 ? "均衡" : $"偏{(value < 0 ? "游戏" : "聊天")} {Math.Abs(value)}%";
    }

    private void Balance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying || _disposed || _client is null || !BalanceSlider.IsEnabled) return;
        // A small central dead zone makes returning to a neutral balance easy.
        if (Mouse.LeftButton == MouseButtonState.Pressed && Math.Abs(e.NewValue) <= 2 && e.NewValue != 0)
        {
            BalanceSlider.Value = 0;
            return;
        }
        UpdateLabel();
        _revision++;
        _pending = (float)(BalanceSlider.Value / 100);
        if (!_writing) _ = WritePendingAsync();
    }

    private async Task WritePendingAsync()
    {
        _writing = true;
        try
        {
            while (!_disposed && _pending.HasValue && _client is not null)
            {
                await Task.Delay(90, _lifetime.Token);
                if (!_pending.HasValue) break;
                var balance = _pending.Value;
                _pending = null;
                var client = _client;
                var revision = _revision;
                try
                {
                    var confirmed = await client.SetChatMixAsync(balance, _lifetime.Token);
                    if (!_disposed && revision == _revision && !_pending.HasValue && !BalanceSlider.IsMouseCaptureWithin)
                        Apply(confirmed);
                }
                catch (OperationCanceledException) when (_disposed) { break; }
                catch (Exception exception)
                {
                    if (!_disposed && revision == _revision)
                    {
                        // Restore the actual value where possible, then keep the failure visible.
                        try { Apply(await client.GetChatMixAsync(_lifetime.Token)); }
                        catch (Exception) { SetEnabled(false); }
                        if (!_disposed) HintText.Text = $"调整失败：{exception.Message}";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposed) { }
        finally { _writing = false; }
    }

    private void Commit()
    {
        if (_disposed || !BalanceSlider.IsEnabled || _client is null) return;
        _revision++;
        _pending = (float)(BalanceSlider.Value / 100);
        if (!_writing) _ = WritePendingAsync();
    }

    private void Balance_MouseUp(object sender, MouseButtonEventArgs e) => Commit();
    private void Balance_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => Commit();
    private void Balance_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Commit();
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        BalanceSlider.Value = 0;
        Commit();
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _lifetime.Cancel();
        _client?.Dispose();
        _lifetime.Dispose();
    }
}
