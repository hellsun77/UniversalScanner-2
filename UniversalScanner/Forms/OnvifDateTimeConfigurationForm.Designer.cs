namespace UniversalScanner.Forms
{
    partial class OnvifDateTimeConfigurationForm
    {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this.groupCurrentTime = new System.Windows.Forms.GroupBox();
            this.labelDifference = new System.Windows.Forms.Label();
            this.labelComputerTime = new System.Windows.Forms.Label();
            this.labelCameraUtcTime = new System.Windows.Forms.Label();
            this.labelCameraLocalTime = new System.Windows.Forms.Label();
            this.groupSyncMode = new System.Windows.Forms.GroupBox();
            this.radioNTP = new System.Windows.Forms.RadioButton();
            this.radioManual = new System.Windows.Forms.RadioButton();
            this.groupManual = new System.Windows.Forms.GroupBox();
            this.buttonSyncComputer = new System.Windows.Forms.Button();
            this.checkDaylightSavings = new System.Windows.Forms.CheckBox();
            this.comboTimeZone = new System.Windows.Forms.ComboBox();
            this.labelTimeZone = new System.Windows.Forms.Label();
            this.dateTimePickerTime = new System.Windows.Forms.DateTimePicker();
            this.dateTimePickerDate = new System.Windows.Forms.DateTimePicker();
            this.labelManualTime = new System.Windows.Forms.Label();
            this.groupNTP = new System.Windows.Forms.GroupBox();
            this.textNtpServer = new System.Windows.Forms.TextBox();
            this.labelNtpServer = new System.Windows.Forms.Label();
            this.buttonApply = new System.Windows.Forms.Button();
            this.buttonRefresh = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.labelStatus = new System.Windows.Forms.Label();
            this.numericNtpPort = new System.Windows.Forms.NumericUpDown();
            this.labelNtpPort = new System.Windows.Forms.Label();
            this.numericNtpInterval = new System.Windows.Forms.NumericUpDown();
            this.labelNtpInterval = new System.Windows.Forms.Label();
            this.labelNtpWarning = new System.Windows.Forms.Label();
            this.groupCurrentTime.SuspendLayout();
            this.groupSyncMode.SuspendLayout();
            this.groupManual.SuspendLayout();
            this.groupNTP.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNtpPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericNtpInterval)).BeginInit();
            this.SuspendLayout();
            // 
            // groupCurrentTime
            // 
            this.groupCurrentTime.Controls.Add(this.labelDifference);
            this.groupCurrentTime.Controls.Add(this.labelComputerTime);
            this.groupCurrentTime.Controls.Add(this.labelCameraUtcTime);
            this.groupCurrentTime.Controls.Add(this.labelCameraLocalTime);
            this.groupCurrentTime.Location = new System.Drawing.Point(12, 12);
            this.groupCurrentTime.Name = "groupCurrentTime";
            this.groupCurrentTime.Size = new System.Drawing.Size(460, 110);
            this.groupCurrentTime.TabIndex = 0;
            this.groupCurrentTime.TabStop = false;
            this.groupCurrentTime.Text = "Current Camera Time";
            // 
            // labelDifference
            // 
            this.labelDifference.AutoSize = true;
            this.labelDifference.Location = new System.Drawing.Point(15, 80);
            this.labelDifference.Name = "labelDifference";
            this.labelDifference.Size = new System.Drawing.Size(59, 13);
            this.labelDifference.TabIndex = 3;
            this.labelDifference.Text = "Difference:";
            // 
            // labelComputerTime
            // 
            this.labelComputerTime.AutoSize = true;
            this.labelComputerTime.Location = new System.Drawing.Point(15, 60);
            this.labelComputerTime.Name = "labelComputerTime";
            this.labelComputerTime.Size = new System.Drawing.Size(81, 13);
            this.labelComputerTime.TabIndex = 2;
            this.labelComputerTime.Text = "Computer Time:";
            // 
            // labelCameraUtcTime
            // 
            this.labelCameraUtcTime.AutoSize = true;
            this.labelCameraUtcTime.Location = new System.Drawing.Point(15, 40);
            this.labelCameraUtcTime.Name = "labelCameraUtcTime";
            this.labelCameraUtcTime.Size = new System.Drawing.Size(95, 13);
            this.labelCameraUtcTime.TabIndex = 1;
            this.labelCameraUtcTime.Text = "Camera UTC Time:";
            // 
            // labelCameraLocalTime
            // 
            this.labelCameraLocalTime.AutoSize = true;
            this.labelCameraLocalTime.Location = new System.Drawing.Point(15, 20);
            this.labelCameraLocalTime.Name = "labelCameraLocalTime";
            this.labelCameraLocalTime.Size = new System.Drawing.Size(101, 13);
            this.labelCameraLocalTime.TabIndex = 0;
            this.labelCameraLocalTime.Text = "Camera Local Time:";
            // 
            // groupSyncMode
            // 
            this.groupSyncMode.Controls.Add(this.radioNTP);
            this.groupSyncMode.Controls.Add(this.radioManual);
            this.groupSyncMode.Location = new System.Drawing.Point(12, 128);
            this.groupSyncMode.Name = "groupSyncMode";
            this.groupSyncMode.Size = new System.Drawing.Size(460, 50);
            this.groupSyncMode.TabIndex = 1;
            this.groupSyncMode.TabStop = false;
            this.groupSyncMode.Text = "Synchronization Mode";
            // 
            // radioNTP
            // 
            this.radioNTP.AutoSize = true;
            this.radioNTP.Location = new System.Drawing.Point(231, 19);
            this.radioNTP.Name = "radioNTP";
            this.radioNTP.Size = new System.Drawing.Size(127, 17);
            this.radioNTP.TabIndex = 1;
            this.radioNTP.TabStop = true;
            this.radioNTP.Text = "Synchronize with NTP";
            this.radioNTP.UseVisualStyleBackColor = true;
            this.radioNTP.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // radioManual
            // 
            this.radioManual.AutoSize = true;
            this.radioManual.Location = new System.Drawing.Point(18, 19);
            this.radioManual.Name = "radioManual";
            this.radioManual.Size = new System.Drawing.Size(133, 17);
            this.radioManual.TabIndex = 0;
            this.radioManual.TabStop = true;
            this.radioManual.Text = "Manual Date and Time";
            this.radioManual.UseVisualStyleBackColor = true;
            this.radioManual.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // groupManual
            // 
            this.groupManual.Controls.Add(this.buttonSyncComputer);
            this.groupManual.Controls.Add(this.checkDaylightSavings);
            this.groupManual.Controls.Add(this.comboTimeZone);
            this.groupManual.Controls.Add(this.labelTimeZone);
            this.groupManual.Controls.Add(this.dateTimePickerTime);
            this.groupManual.Controls.Add(this.dateTimePickerDate);
            this.groupManual.Controls.Add(this.labelManualTime);
            this.groupManual.Location = new System.Drawing.Point(12, 184);
            this.groupManual.Name = "groupManual";
            this.groupManual.Size = new System.Drawing.Size(460, 120);
            this.groupManual.TabIndex = 2;
            this.groupManual.TabStop = false;
            this.groupManual.Text = "Manual Settings";
            // 
            // buttonSyncComputer
            // 
            this.buttonSyncComputer.Location = new System.Drawing.Point(231, 80);
            this.buttonSyncComputer.Name = "buttonSyncComputer";
            this.buttonSyncComputer.Size = new System.Drawing.Size(199, 23);
            this.buttonSyncComputer.TabIndex = 6;
            this.buttonSyncComputer.Text = "Synchronize to Computer";
            this.buttonSyncComputer.UseVisualStyleBackColor = true;
            this.buttonSyncComputer.Click += new System.EventHandler(this.buttonSyncComputer_Click);
            // 
            // checkDaylightSavings
            // 
            this.checkDaylightSavings.AutoSize = true;
            this.checkDaylightSavings.Location = new System.Drawing.Point(231, 55);
            this.checkDaylightSavings.Name = "checkDaylightSavings";
            this.checkDaylightSavings.Size = new System.Drawing.Size(147, 17);
            this.checkDaylightSavings.TabIndex = 5;
            this.checkDaylightSavings.Text = "Enable Daylight Savings";
            this.checkDaylightSavings.UseVisualStyleBackColor = true;
            // 
            // comboTimeZone
            // 
            this.comboTimeZone.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboTimeZone.FormattingEnabled = true;
            this.comboTimeZone.Location = new System.Drawing.Point(90, 53);
            this.comboTimeZone.Name = "comboTimeZone";
            this.comboTimeZone.Size = new System.Drawing.Size(130, 21);
            this.comboTimeZone.TabIndex = 4;
            // 
            // labelTimeZone
            // 
            this.labelTimeZone.AutoSize = true;
            this.labelTimeZone.Location = new System.Drawing.Point(15, 56);
            this.labelTimeZone.Name = "labelTimeZone";
            this.labelTimeZone.Size = new System.Drawing.Size(61, 13);
            this.labelTimeZone.TabIndex = 3;
            this.labelTimeZone.Text = "Time Zone:";
            // 
            // dateTimePickerTime
            // 
            this.dateTimePickerTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.dateTimePickerTime.Location = new System.Drawing.Point(231, 23);
            this.dateTimePickerTime.Name = "dateTimePickerTime";
            this.dateTimePickerTime.ShowUpDown = true;
            this.dateTimePickerTime.Size = new System.Drawing.Size(100, 20);
            this.dateTimePickerTime.TabIndex = 2;
            // 
            // dateTimePickerDate
            // 
            this.dateTimePickerDate.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dateTimePickerDate.Location = new System.Drawing.Point(90, 23);
            this.dateTimePickerDate.Name = "dateTimePickerDate";
            this.dateTimePickerDate.Size = new System.Drawing.Size(130, 20);
            this.dateTimePickerDate.TabIndex = 1;
            // 
            // labelManualTime
            // 
            this.labelManualTime.AutoSize = true;
            this.labelManualTime.Location = new System.Drawing.Point(15, 26);
            this.labelManualTime.Name = "labelManualTime";
            this.labelManualTime.Size = new System.Drawing.Size(61, 13);
            this.labelManualTime.TabIndex = 0;
            this.labelManualTime.Text = "Date/Time:";
            // 
            // groupNTP
            // 
            this.groupNTP.Controls.Add(this.textNtpServer);
            this.groupNTP.Controls.Add(this.labelNtpServer);
            this.groupNTP.Controls.Add(this.numericNtpInterval);
            this.groupNTP.Controls.Add(this.labelNtpInterval);
            this.groupNTP.Controls.Add(this.numericNtpPort);
            this.groupNTP.Controls.Add(this.labelNtpPort);
            this.groupNTP.Controls.Add(this.labelNtpWarning);
            this.groupNTP.Location = new System.Drawing.Point(12, 310);
            this.groupNTP.Name = "groupNTP";
            this.groupNTP.Size = new System.Drawing.Size(460, 105);
            this.groupNTP.TabIndex = 3;
            this.groupNTP.TabStop = false;
            this.groupNTP.Text = "NTP Settings";
            // 
            // textNtpServer
            // 
            this.textNtpServer.Location = new System.Drawing.Point(90, 23);
            this.textNtpServer.Name = "textNtpServer";
            this.textNtpServer.Size = new System.Drawing.Size(241, 20);
            this.textNtpServer.TabIndex = 1;
            // 
            // labelNtpServer
            // 
            this.labelNtpServer.AutoSize = true;
            this.labelNtpServer.Location = new System.Drawing.Point(15, 26);
            this.labelNtpServer.Name = "labelNtpServer";
            this.labelNtpServer.Size = new System.Drawing.Size(66, 13);
            this.labelNtpServer.TabIndex = 0;
            this.labelNtpServer.Text = "NTP Server:";
            // 
            // numericNtpPort
            // 
            this.numericNtpPort.Location = new System.Drawing.Point(90, 50);
            this.numericNtpPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numericNtpPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericNtpPort.Name = "numericNtpPort";
            this.numericNtpPort.Size = new System.Drawing.Size(80, 20);
            this.numericNtpPort.TabIndex = 3;
            this.numericNtpPort.Value = new decimal(new int[] { 123, 0, 0, 0 });
            // 
            // labelNtpPort
            // 
            this.labelNtpPort.AutoSize = true;
            this.labelNtpPort.Location = new System.Drawing.Point(15, 52);
            this.labelNtpPort.Name = "labelNtpPort";
            this.labelNtpPort.Size = new System.Drawing.Size(29, 13);
            this.labelNtpPort.TabIndex = 2;
            this.labelNtpPort.Text = "Port:";
            // 
            // numericNtpInterval
            // 
            this.numericNtpInterval.Location = new System.Drawing.Point(260, 50);
            this.numericNtpInterval.Maximum = new decimal(new int[] { 86400, 0, 0, 0 });
            this.numericNtpInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericNtpInterval.Name = "numericNtpInterval";
            this.numericNtpInterval.Size = new System.Drawing.Size(80, 20);
            this.numericNtpInterval.TabIndex = 5;
            this.numericNtpInterval.Value = new decimal(new int[] { 600, 0, 0, 0 });
            // 
            // labelNtpInterval
            // 
            this.labelNtpInterval.AutoSize = true;
            this.labelNtpInterval.Location = new System.Drawing.Point(180, 52);
            this.labelNtpInterval.Name = "labelNtpInterval";
            this.labelNtpInterval.Size = new System.Drawing.Size(65, 13);
            this.labelNtpInterval.TabIndex = 4;
            this.labelNtpInterval.Text = "Interval (s):";
            // 
            // labelNtpWarning
            // 
            this.labelNtpWarning.AutoSize = true;
            this.labelNtpWarning.ForeColor = System.Drawing.Color.Red;
            this.labelNtpWarning.Location = new System.Drawing.Point(15, 80);
            this.labelNtpWarning.Name = "labelNtpWarning";
            this.labelNtpWarning.Size = new System.Drawing.Size(350, 13);
            this.labelNtpWarning.TabIndex = 6;
            this.labelNtpWarning.Text = "This camera does not expose NTP Port or Update Interval through standard ONVIF.";
            // 
            // buttonApply
            // 
            this.buttonApply.Location = new System.Drawing.Point(316, 430);
            this.buttonApply.Name = "buttonApply";
            this.buttonApply.Size = new System.Drawing.Size(75, 25);
            this.buttonApply.TabIndex = 4;
            this.buttonApply.Text = "Apply";
            this.buttonApply.UseVisualStyleBackColor = true;
            this.buttonApply.Click += new System.EventHandler(this.buttonApply_Click);
            // 
            // buttonRefresh
            // 
            this.buttonRefresh.Location = new System.Drawing.Point(235, 430);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(75, 25);
            this.buttonRefresh.TabIndex = 5;
            this.buttonRefresh.Text = "Refresh";
            this.buttonRefresh.UseVisualStyleBackColor = true;
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Location = new System.Drawing.Point(397, 430);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 25);
            this.buttonCancel.TabIndex = 6;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // labelStatus
            // 
            this.labelStatus.AutoSize = true;
            this.labelStatus.Location = new System.Drawing.Point(12, 435);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(40, 13);
            this.labelStatus.TabIndex = 7;
            this.labelStatus.Text = "Ready";
            // 
            // OnvifDateTimeConfigurationForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 467);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonRefresh);
            this.Controls.Add(this.buttonApply);
            this.Controls.Add(this.groupNTP);
            this.Controls.Add(this.groupManual);
            this.Controls.Add(this.groupSyncMode);
            this.Controls.Add(this.groupCurrentTime);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OnvifDateTimeConfigurationForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "ONVIF Date and Time Configuration";
            this.Load += new System.EventHandler(this.OnvifDateTimeConfigurationForm_Load);
            this.groupCurrentTime.ResumeLayout(false);
            this.groupCurrentTime.PerformLayout();
            this.groupSyncMode.ResumeLayout(false);
            this.groupSyncMode.PerformLayout();
            this.groupManual.ResumeLayout(false);
            this.groupManual.PerformLayout();
            this.groupNTP.ResumeLayout(false);
            this.groupNTP.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNtpPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericNtpInterval)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.GroupBox groupCurrentTime;
        private System.Windows.Forms.Label labelCameraLocalTime;
        private System.Windows.Forms.Label labelDifference;
        private System.Windows.Forms.Label labelComputerTime;
        private System.Windows.Forms.Label labelCameraUtcTime;
        private System.Windows.Forms.GroupBox groupSyncMode;
        private System.Windows.Forms.RadioButton radioNTP;
        private System.Windows.Forms.RadioButton radioManual;
        private System.Windows.Forms.GroupBox groupManual;
        private System.Windows.Forms.CheckBox checkDaylightSavings;
        private System.Windows.Forms.ComboBox comboTimeZone;
        private System.Windows.Forms.Label labelTimeZone;
        private System.Windows.Forms.DateTimePicker dateTimePickerTime;
        private System.Windows.Forms.DateTimePicker dateTimePickerDate;
        private System.Windows.Forms.Label labelManualTime;
        private System.Windows.Forms.Button buttonSyncComputer;
        private System.Windows.Forms.GroupBox groupNTP;
        private System.Windows.Forms.TextBox textNtpServer;
        private System.Windows.Forms.Label labelNtpServer;
        private System.Windows.Forms.Button buttonApply;
        private System.Windows.Forms.Button buttonRefresh;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.NumericUpDown numericNtpPort;
        private System.Windows.Forms.Label labelNtpPort;
        private System.Windows.Forms.NumericUpDown numericNtpInterval;
        private System.Windows.Forms.Label labelNtpInterval;
        private System.Windows.Forms.Label labelNtpWarning;
    }
}
