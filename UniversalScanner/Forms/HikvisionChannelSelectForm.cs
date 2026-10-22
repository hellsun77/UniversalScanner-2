using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Hikvision;

namespace UniversalScanner.Forms
{
    public class HikvisionChannelSelectForm : Form
    {
        private static bool _preferSubStream = false;

        private Label messageLabel;
        private Label channelLabel;
        private ComboBox cameraComboBox;
        private NumericUpDown manualChannelNum;
        
        private RadioButton mainStreamRadio;
        private RadioButton subStreamRadio;
        
        private Button openButton;
        private Button cancelButton;
        private Button openDefaultButton;

        private readonly List<HikvisionChannel> _channels;
        private readonly bool _isFallback;

        public int SelectedStreamId { get; private set; }
        public bool OpenDefault { get; private set; }

        public HikvisionChannelSelectForm(List<HikvisionChannel> channels, bool isFallback)
            : this(channels, isFallback, _preferSubStream)
        {
        }

        public HikvisionChannelSelectForm(List<HikvisionChannel> channels, bool isFallback, bool preferSubStream)
        {
            _channels = channels;
            _isFallback = isFallback;
            _preferSubStream = preferSubStream;

            Text = "Open Live Stream";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(380, _isFallback ? 250 : 210);

            BuildUi();
            UniversalScanner.AppVisualTheme.Apply(this);
        }

        private void BuildUi()
        {
            int top = 20;

            if (_isFallback)
            {
                messageLabel = new Label
                {
                    Text = "Universal Scanner could not retrieve the Hikvision channel list.\r\nSelect a channel manually or open the default channel.",
                    Left = 20,
                    Top = top,
                    Width = 340,
                    Height = 40,
                    ForeColor = Color.DarkRed
                };
                Controls.Add(messageLabel);
                top += 50;
            }

            channelLabel = new Label
            {
                Text = "Camera:",
                Left = 20,
                Top = top + 4,
                Width = 70
            };
            Controls.Add(channelLabel);

            if (_isFallback)
            {
                manualChannelNum = new NumericUpDown
                {
                    Left = 90,
                    Top = top,
                    Width = 100,
                    Minimum = 1,
                    Maximum = 9999, // safe range, prevents overflow when multiplying by 100
                    Value = 1
                };
                Controls.Add(manualChannelNum);
            }
            else
            {
                cameraComboBox = new ComboBox
                {
                    Left = 90,
                    Top = top,
                    Width = 260,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (var ch in _channels)
                {
                    cameraComboBox.Items.Add($"Camera {ch.PhysicalChannel}");
                }
                cameraComboBox.SelectedIndexChanged += CameraComboBox_SelectedIndexChanged;
                Controls.Add(cameraComboBox);
            }

            top += 40;
            Label streamLabel = new Label
            {
                Text = "Stream:",
                Left = 20,
                Top = top + 2,
                Width = 70
            };
            Controls.Add(streamLabel);

            mainStreamRadio = new RadioButton
            {
                Text = "Main Stream",
                Left = 90,
                Top = top,
                Width = 120,
                Checked = !_preferSubStream
            };
            
            subStreamRadio = new RadioButton
            {
                Text = "Sub Stream",
                Left = 210,
                Top = top,
                Width = 120,
                Checked = _preferSubStream
            };
            
            Controls.Add(mainStreamRadio);
            Controls.Add(subStreamRadio);

            top += 50;

            openButton = new Button
            {
                Text = "Open VLC",
                Left = 190,
                Top = top,
                Width = 80
            };
            openButton.Click += OpenButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Left = 280,
                Top = top,
                Width = 70,
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(openButton);
            Controls.Add(cancelButton);

            if (_isFallback)
            {
                openDefaultButton = new Button
                {
                    Text = "Open default channel",
                    Left = 20,
                    Top = top,
                    Width = 150
                };
                openDefaultButton.Click += (s, e) =>
                {
                    OpenDefault = true;
                    SelectedStreamId = 101;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(openDefaultButton);
            }

            AcceptButton = openButton;
            CancelButton = cancelButton;

            if (!_isFallback && cameraComboBox.Items.Count > 0)
            {
                cameraComboBox.SelectedIndex = 0;
            }
        }

        private void CameraComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isFallback || cameraComboBox.SelectedIndex < 0) return;

            var ch = _channels[cameraComboBox.SelectedIndex];
            
            bool hasMain = ch.SupportedStreamTypes.Contains(1);
            bool hasSub = ch.SupportedStreamTypes.Contains(2);
            
            mainStreamRadio.Enabled = hasMain;
            subStreamRadio.Enabled = hasSub;

            if (_preferSubStream && hasSub)
            {
                subStreamRadio.Checked = true;
            }
            else if (hasMain)
            {
                mainStreamRadio.Checked = true;
            }
            else if (hasSub)
            {
                subStreamRadio.Checked = true;
            }
            else
            {
                // Edge case: neither 1 nor 2 is technically present. 
                // We shouldn't block the user, just check whatever we can.
                mainStreamRadio.Enabled = true;
                mainStreamRadio.Checked = true;
            }
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (mainStreamRadio.Checked) _preferSubStream = false;
            if (subStreamRadio.Checked) _preferSubStream = true;

            int streamType = mainStreamRadio.Checked ? 1 : 2;

            if (_isFallback)
            {
                int phys = (int)manualChannelNum.Value;
                try
                {
                    SelectedStreamId = checked((phys * 100) + streamType);
                }
                catch (OverflowException)
                {
                    MessageBox.Show(this, "Channel number is too large.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                if (cameraComboBox.SelectedIndex < 0) return;
                var ch = _channels[cameraComboBox.SelectedIndex];
                
                // If they picked Main but it's disabled, we shouldn't really be here, but just in case
                int phys = ch.PhysicalChannel;
                
                // Check if they want main/sub but neither 1 nor 2 is strictly in SupportedStreamTypes.
                // We just build the ID based on 1 or 2 as fallback if the device had e.g. 03 only.
                // The requirements state: "Enable Main Stream only when stream type 01 exists. Enable Sub Stream only when stream type 02 exists."
                // "If main/sub is unavailable, first valid returned stream ID."
                if (!ch.SupportedStreamTypes.Contains(streamType))
                {
                    // If the selected radio button stream type is not supported, just pick the first supported one
                    if (ch.SupportedStreamTypes.Count > 0)
                    {
                        streamType = ch.SupportedStreamTypes[0];
                    }
                }

                try
                {
                    SelectedStreamId = checked((phys * 100) + streamType);
                }
                catch (OverflowException)
                {
                    SelectedStreamId = 101;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
