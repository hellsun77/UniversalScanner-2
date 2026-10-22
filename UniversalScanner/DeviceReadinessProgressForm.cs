using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UniversalScanner
{
    public partial class DeviceReadinessProgressForm : Form
    {
        private readonly Func<Action<string>, Func<bool>, Task> _verificationTask;
        private bool _isCancelled = false;

        public DeviceReadinessProgressForm(Func<Action<string>, Func<bool>, Task> verificationTask)
        {
            InitializeComponent();
            _verificationTask = verificationTask;
            AppVisualTheme.Apply(this);
        }

        private async void DeviceReadinessProgressForm_Load(object sender, EventArgs e)
        {
            try
            {
                await _verificationTask(
                    msg =>
                    {
                        if (InvokeRequired)
                        {
                            Invoke(new Action(() => lblStatus.Text = msg));
                        }
                        else
                        {
                            lblStatus.Text = msg;
                        }
                    },
                    () => _isCancelled
                );
            }
            finally
            {
                this.DialogResult = _isCancelled ? DialogResult.Cancel : DialogResult.OK;
                this.Close();
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _isCancelled = true;
            btnCancel.Enabled = false;
            lblStatus.Text = "Cancelling verification...";
        }
    }
}
