using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.MotionDetection;

namespace UniversalScanner.Forms
{
    public partial class MotionDetectionForm : Form
    {
        private readonly IMotionDetectionService _service;
        private readonly string _ipAddress;
        private readonly string _vendor;
        private readonly string _model;
        private string _username;
        private string _password;

        private MotionGridControl _gridControl;
        private MotionDetectionConfiguration _currentConfig;
        
        private List<PointF> _polygon = null;
        private bool _supportsPolygon = false;
        private int _dragVertexIndex = -1;
        private bool _isApplying = false;

        public MotionDetectionForm(IMotionDetectionService service, string ipAddress, string vendor, string model = "")
        {
            _service = service;
            _ipAddress = ipAddress;
            _vendor = vendor;
            _model = model;
            
            InitializeComponent();
            InitializeCustomGrid();
            
            pnlGrid.MouseDown += PnlGrid_MouseDown;
            pnlGrid.MouseMove += PnlGrid_MouseMove;
            pnlGrid.MouseUp += PnlGrid_MouseUp;
            
            lblIpAddress.Text = $"IP Address: {_ipAddress}";
            lblVendor.Text = $"Vendor: {_vendor}";
            
            this.Load += MotionDetectionForm_Load;
            UniversalScanner.AppVisualTheme.Apply(this);
        }

        private void InitializeCustomGrid()
        {
            _gridControl = new MotionGridControl();
            _gridControl.Dock = DockStyle.Fill;
            _gridControl.GridChanged += GridControl_GridChanged;
            pnlGrid.Controls.Add(_gridControl);
            pnlGrid.Paint += PnlGrid_Paint;
        }

        private void PnlGrid_Paint(object sender, PaintEventArgs e)
        {
            if (_supportsPolygon)
            {
                if (_polygon != null && _polygon.Count > 1)
                {
                    using (var fillBrush = new SolidBrush(Color.FromArgb(100, Color.Red)))
                    using (var p = new Pen(Color.Red, 2f))
                    using (var handleBrush = new SolidBrush(Color.White))
                    using (var handlePen = new Pen(Color.Black, 1f))
                    {
                        var points = _polygon.Select(pt => new PointF(pt.X * pnlGrid.Width, pt.Y * pnlGrid.Height)).ToArray();
                        e.Graphics.FillPolygon(fillBrush, points);
                        e.Graphics.DrawPolygon(p, points);
                        
                        foreach (var pt in points)
                        {
                            var rect = new RectangleF(pt.X - 4f, pt.Y - 4f, 8f, 8f);
                            e.Graphics.FillRectangle(handleBrush, rect);
                            e.Graphics.DrawRectangle(handlePen, rect.X, rect.Y, rect.Width, rect.Height);
                        }
                    }
                }
                else
                {
                    using (var brush = new SolidBrush(Color.White))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.DrawString("No polygon returned by camera.", this.Font, brush, new RectangleF(0, 0, pnlGrid.Width, pnlGrid.Height), format);
                    }
                }
            }
        }

        private void PnlGrid_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_supportsPolygon || _polygon == null || _isApplying) return;

            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < _polygon.Count; i++)
                {
                    float px = _polygon[i].X * pnlGrid.Width;
                    float py = _polygon[i].Y * pnlGrid.Height;
                    float distSq = (e.X - px) * (e.X - px) + (e.Y - py) * (e.Y - py);
                    if (distSq <= 100f) // 10px radius
                    {
                        _dragVertexIndex = i;
                        break;
                    }
                }
            }
        }

        private void PnlGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_supportsPolygon || _polygon == null || _isApplying) return;

            if (_dragVertexIndex >= 0 && _dragVertexIndex < _polygon.Count)
            {
                float newX = Math.Max(0f, Math.Min(1f, (float)e.X / pnlGrid.Width));
                float newY = Math.Max(0f, Math.Min(1f, (float)e.Y / pnlGrid.Height));
                _polygon[_dragVertexIndex] = new PointF(newX, newY);
                pnlGrid.Invalidate();
            }
        }

        private void PnlGrid_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragVertexIndex = -1;
            }
        }

        private async void MotionDetectionForm_Load(object sender, EventArgs e)
        {
            OnvifTraceLogger.SetCorrelationId("MD");
            string pwdSource = "Universal Login Cache";
            if (!UniversalLoginManager.HasUniversalCredentials())
            {
                using (var loginForm = new UniversalLoginForm())
                {
                    if (loginForm.ShowDialog(this) != DialogResult.OK)
                    {
                        this.Close();
                        return;
                    }
                    pwdSource = "Universal Login Dialog";
                }
            }
            UniversalLoginManager.GetUniversalCredentials(out _username, out _password);
            OnvifTraceLogger.LogRequest(
                "MotionDetectionForm",
                "MotionDetectionForm",
                "Load",
                "None",
                "Initialization",
                "N/A",
                "N/A",
                _username,
                pwdSource,
                (_password ?? "").Length,
                "N/A",
                "None",
                0,
                false,
                false,
                0);

            await LoadConfigurationAsync();
        }

        private async Task LoadConfigurationAsync()
        {
            SetLoading(true, "Reading motion configuration from camera...");
            try
            {
                _currentConfig = await _service.ReadConfigurationAsync(_ipAddress, _username, _password);
                var caps = _service.GetCapabilities();
                
                if (!_currentConfig.IsSupported)
                {
                    lblStatus.Text = "Status: Unsupported";
                    lblStatus.ForeColor = Color.Red;
                    MessageBox.Show(this, "Motion detection configuration via ONVIF is not supported or failed to read.", "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnApply.Enabled = false;
                    return;
                }

                if (!caps.SupportsEnableDisable)
                {
                    chkEnabled.Enabled = false;
                    chkEnabled.Checked = _currentConfig.Enabled;
                    lblStatus.Text = "ℹ️ Motion Detection enable/disable is not available\nthrough ONVIF on this camera.\n\nMotion Region (Polygon) editing is fully supported.";
                    lblStatus.ForeColor = Color.DarkSlateBlue;
                    lblRegion.Top = 85;
                    lblGridInfo.Top = 105;
                    grpStatus.Height = lblGridInfo.Bottom + 10;
                }
                else
                {
                    lblStatus.Text = $"Status: {(_currentConfig.Enabled ? "Enabled" : "Disabled")}";
                    lblStatus.ForeColor = _currentConfig.Enabled ? Color.Green : Color.Black;
                    chkEnabled.Checked = _currentConfig.Enabled;
                    chkEnabled.Enabled = true;
                    lblRegion.Top = 55;
                    lblGridInfo.Top = 80;
                    grpStatus.Height = 120;
                }
                
                lblRegion.Text = $"Region: {(_currentConfig.IsFullImage ? "Full Image" : "Partial Image")}";
                lblGridInfo.Text = $"Grid: {_currentConfig.Columns} × {_currentConfig.Rows}";
                lblModel.Text = $"Model: {(string.IsNullOrWhiteSpace(_model) ? "Unknown" : _model)}";
                lblChannel.Text = $"Channel: {_currentConfig.Channel}";
                lblConfigSource.Text = $"Configuration Source: {_currentConfig.ConfigurationSource}";

                if (!caps.SupportsCustomGrid)
                {
                    _gridControl.IsReadOnly = true;
                    btnClear.Enabled = false;
                    btnSelectAll.Text = "Full Image";
                    lblStatus.Text += " | Custom region editing is not supported by this camera through ONVIF.";
                }
                else
                {
                    _gridControl.IsReadOnly = false;
                    btnClear.Enabled = caps.SupportsClearArea;
                    btnSelectAll.Text = "Select All";
                }
                
                if (_currentConfig.Sensitivity.HasValue)
                {
                    numSensitivity.Minimum = _currentConfig.SensitivityMinimum ?? 0;
                    numSensitivity.Maximum = _currentConfig.SensitivityMaximum ?? 100;
                    numSensitivity.Value = _currentConfig.Sensitivity.Value;
                    numSensitivity.Enabled = caps.SupportsSensitivity;
                }
                else
                {
                    numSensitivity.Enabled = false;
                }

                if (caps.SupportsPolygon)
                {
                    _supportsPolygon = true;
                    lblGridInfo.Text = "Motion Region: Polygon";
                    btnApply.Enabled = true;

                    _polygon = _currentConfig.Polygon;
                    _gridControl.Visible = false;
                    pnlGrid.Invalidate();
                    radFullImage.Enabled = false;
                    radClearArea.Enabled = false;
                    radKeepExisting.Enabled = false;
                    radKeepExisting.Checked = true;
                    btnSelectAll.Enabled = false;
                    btnClear.Enabled = false;
                    
                    pnlGrid.Invalidate();
                }
                else
                {
                    _supportsPolygon = false;
                    _polygon = null;
                    _gridControl.Visible = true;
                    _gridControl.UpdateGrid(_currentConfig.Columns ?? 22, _currentConfig.Rows ?? 18, _currentConfig.ActiveCells);
                    radKeepExisting.Checked = true;
                }
                
                btnApply.Enabled = caps.SupportsWrite;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Read Error";
                lblStatus.ForeColor = Color.Red;
                MessageBox.Show(this, $"Error reading configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnApply.Enabled = false;
            }
            finally
            {
                SetLoading(false, "Ready");
            }
        }

        private void SetLoading(bool loading, string status)
        {
            var caps = _service?.GetCapabilities();
            bool supportsSettings = caps?.SupportsEnableDisable ?? true;

            grpControls.Enabled = !loading && supportsSettings;
            btnRead.Enabled = !loading;
            btnApply.Enabled = !loading && _currentConfig != null && _currentConfig.IsSupported && caps.SupportsWrite;
            btnTestEventMonitor.Enabled = !loading;
            btnSelectAll.Enabled = !loading && supportsSettings;
            btnClear.Enabled = !loading && caps.SupportsClearArea && caps.SupportsCustomGrid && supportsSettings;
            _gridControl.Enabled = !loading;
            
            if (loading) lblStatus.Text = status;
        }

        private async void btnRead_Click(object sender, EventArgs e)
        {
            await LoadConfigurationAsync();
        }

        private void btnSelectAll_Click(object sender, EventArgs e)
        {
            _gridControl.SelectAll();
            radKeepExisting.Checked = true;
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            _gridControl.ClearAll();
            radKeepExisting.Checked = true;
        }

        private void GridControl_GridChanged(object sender, EventArgs e)
        {
            radKeepExisting.Checked = true;
        }

        private async void btnApply_Click(object sender, EventArgs e)
        {
            _isApplying = true;
            try
            {
                bool[] activeCellsToApply = null;
                if (!_supportsPolygon)
                {
                    if (radFullImage.Checked)
                    {
                        activeCellsToApply = new bool[_currentConfig.Columns.Value * _currentConfig.Rows.Value];
                        for (int i = 0; i < activeCellsToApply.Length; i++) activeCellsToApply[i] = true;
                    }
                    else if (radClearArea.Checked)
                    {
                        activeCellsToApply = new bool[_currentConfig.Columns.Value * _currentConfig.Rows.Value];
                    }
                    else
                    {
                        activeCellsToApply = _gridControl.GetActiveCells();
                    }
                }

                var request = new MotionDetectionApplyRequest
                {
                    Enabled = chkEnabled.Checked,
                    ActiveCells = activeCellsToApply,
                    Sensitivity = numSensitivity.Enabled ? (int)numSensitivity.Value : (int?)null,
                    Polygon = _supportsPolygon ? _polygon : null
                };

                SetLoading(true, "Applying configuration to camera...");

                var result = await _service.ApplyConfigurationAsync(_ipAddress, _username, _password, request);

                if (result.Success)
                {
                    MessageBox.Show(this, "Motion detection configuration successfully applied and verified.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadConfigurationAsync();
                }
                else
                {
                    string msg = $"Configuration failed.\r\nClassification: {result.ErrorClassification}\r\nMessage: {result.ErrorMessage}";
                    
                    if (result.ErrorClassification == "Custom motion-region write rejected by camera")
                    {
                        MessageBox.Show(this, msg, "Apply Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        await LoadConfigurationAsync();
                    }
                    else if (result.ErrorClassification == "Verification Failed")
                    {
                        msg += "\r\n\r\nWould you like to restore the previous camera settings?";
                        var dlg = MessageBox.Show(this, msg, "Apply Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (dlg == DialogResult.Yes)
                        {
                            SetLoading(true, "Rolling back settings...");
                            var rb = await _service.RestoreRollbackAsync(_ipAddress, _username, _password);
                            if (rb.Success) MessageBox.Show(this, "Rollback successful.", "Rollback", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            else MessageBox.Show(this, $"Rollback failed: {rb.ErrorMessage}", "Rollback Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show(this, msg, "Apply Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            finally
            {
                SetLoading(false, "Ready");
                _isApplying = false;
            }
        }

        private void btnTestEventMonitor_Click(object sender, EventArgs e)
        {
            try
            {
                var eventForm = new OnvifEventMonitorForm(_ipAddress, 80, _username, _password, _vendor, "Unknown", "Unknown");
                eventForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open Event Monitor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }

    public class MotionGridControl : Control
    {
        private int _cols = 22;
        private int _rows = 18;
        private bool[] _cells = new bool[22 * 18];
        private bool _isDragging = false;
        private bool _dragState = false;
        private bool _isReadOnly = false;

        public bool IsReadOnly
        {
            get { return _isReadOnly; }
            set 
            { 
                _isReadOnly = value;
                this.Cursor = _isReadOnly ? Cursors.Default : Cursors.Cross;
            }
        }

        public event EventHandler GridChanged;

        public MotionGridControl()
        {
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
        }

        public void UpdateGrid(int cols, int rows, bool[] cells)
        {
            _cols = cols;
            _rows = rows;
            _cells = cells;
            if (_cells == null || _cells.Length != cols * rows)
            {
                _cells = new bool[cols * rows];
            }
            this.Invalidate();
        }

        public void SelectAll()
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = true;
            this.Invalidate();
            GridChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = false;
            this.Invalidate();
            GridChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool[] GetActiveCells()
        {
            bool[] copy = new bool[_cells.Length];
            Array.Copy(_cells, copy, _cells.Length);
            return copy;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            if (_cols <= 0 || _rows <= 0) return;

            float cellW = (float)this.Width / _cols;
            float cellH = (float)this.Height / _rows;

            using (Brush activeBrush = new SolidBrush(Color.FromArgb(100, Color.LimeGreen)))
            using (Pen gridPen = new Pen(Color.FromArgb(40, Color.White)))
            {
                for (int r = 0; r < _rows; r++)
                {
                    for (int c = 0; c < _cols; c++)
                    {
                        int index = r * _cols + c;
                        float x = c * cellW;
                        float y = r * cellH;

                        if (index < _cells.Length && _cells[index])
                        {
                            g.FillRectangle(activeBrush, x, y, cellW, cellH);
                        }
                        
                        g.DrawRectangle(gridPen, x, y, cellW, cellH);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_isReadOnly) return;
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                int index = GetCellIndex(e.X, e.Y);
                if (index >= 0 && index < _cells.Length)
                {
                    _dragState = !_cells[index];
                    _cells[index] = _dragState;
                    this.Invalidate();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isReadOnly) return;
            if (_isDragging)
            {
                int index = GetCellIndex(e.X, e.Y);
                if (index >= 0 && index < _cells.Length)
                {
                    if (_cells[index] != _dragState)
                    {
                        _cells[index] = _dragState;
                        this.Invalidate();
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isReadOnly) return;
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                GridChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private int GetCellIndex(int x, int y)
        {
            if (_cols <= 0 || _rows <= 0) return -1;
            if (x < 0 || x >= this.Width || y < 0 || y >= this.Height) return -1;

            int c = (int)(x / ((float)this.Width / _cols));
            int r = (int)(y / ((float)this.Height / _rows));
            
            if (c >= _cols) c = _cols - 1;
            if (r >= _rows) r = _rows - 1;

            return r * _cols + c;
        }
    }
}
