using AudioActivityDetector.CoreAudio;

namespace AudioActivityDetector
{
    public class MainForm : Form
    {
        // Peak level (0.0-1.0) above which we consider the system to be
        // producing sound. Windows' own idle noise floor is 0.0, so almost
        // any positive value indicates real signal, but we keep a small
        // margin to avoid reacting to digital noise/rounding.
        private const float PeakThreshold = 0.005f;

        // How long to keep reporting "Playing" after the last loud sample,
        // so brief gaps between audio chunks don't cause flicker.
        private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(400);

        private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 100 };
        private DefaultOutputMeter? _meter;
        private DateTime _lastLoudAt = DateTime.MinValue;

        private readonly Label _statusLabel = new();
        private readonly ProgressBar _peakBar = new();
        private readonly Label _peakValueLabel = new();
        private readonly Label _hintLabel = new();

        public MainForm()
        {
            Text = "Audio Activity Detector";
            Width = 420;
            Height = 220;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            _statusLabel.Text = "Checking...";
            _statusLabel.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            _statusLabel.AutoSize = false;
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 90;

            _peakBar.Dock = DockStyle.Top;
            _peakBar.Height = 24;
            _peakBar.Minimum = 0;
            _peakBar.Maximum = 1000;
            _peakBar.Margin = new Padding(20, 0, 20, 0);

            _peakValueLabel.Dock = DockStyle.Top;
            _peakValueLabel.Height = 24;
            _peakValueLabel.TextAlign = ContentAlignment.MiddleCenter;
            _peakValueLabel.Text = "Peak: 0.000";

            _hintLabel.Dock = DockStyle.Top;
            _hintLabel.Height = 30;
            _hintLabel.TextAlign = ContentAlignment.MiddleCenter;
            _hintLabel.ForeColor = Color.Gray;
            _hintLabel.Text = "Reads the default playback device's peak meter (WASAPI)";

            Controls.Add(_peakValueLabel);
            Controls.Add(_peakBar);
            Controls.Add(_hintLabel);
            Controls.Add(_statusLabel);

            Load += OnLoad;
            FormClosed += OnFormClosed;
            _pollTimer.Tick += OnPollTick;
        }

        private void OnLoad(object? sender, EventArgs e)
        {
            try
            {
                _meter = new DefaultOutputMeter();
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error";
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = "Could not access the audio engine: " + ex.Message;
            }
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            if (_meter == null) return;

            float peak;
            try
            {
                peak = _meter.GetPeakValue();
            }
            catch
            {
                // Transient failure (e.g. device switch in progress) - skip this tick.
                return;
            }

            _peakBar.Value = Math.Clamp((int)(peak * _peakBar.Maximum), 0, _peakBar.Maximum);
            _peakValueLabel.Text = $"Peak: {peak:0.000}";

            if (peak > PeakThreshold)
            {
                _lastLoudAt = DateTime.UtcNow;
            }

            bool isPlaying = (DateTime.UtcNow - _lastLoudAt) <= HoldTime;

            if (isPlaying)
            {
                _statusLabel.Text = "\u266A Audio Playing";
                _statusLabel.ForeColor = Color.SeaGreen;
            }
            else
            {
                _statusLabel.Text = "Silent";
                _statusLabel.ForeColor = Color.DimGray;
            }
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            _pollTimer.Stop();
            _meter?.Dispose();
        }
    }
}
