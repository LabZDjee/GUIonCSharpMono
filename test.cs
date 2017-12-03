using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Ports;

namespace testDjee
{
  public class MainForm : Form
  {
    private Button buttonFiledialog, buttonSend;
    private ListBox listBox;
    private TextBox textBox;
    public MainForm()
    {
      SuspendLayout();
      ClientSize = new System.Drawing.Size(350, 250);
      MaximizeBox = false;
      Text = "Djee / Tests on Mono CS with Forms";
      FormBorderStyle = FormBorderStyle.FixedDialog;
      buttonFiledialog = new Button();
      buttonFiledialog.Location = new System.Drawing.Point(40, 32);
      buttonFiledialog.Text = "Select";
      buttonFiledialog.Click += new System.EventHandler(OnClick);
      Controls.Add(buttonFiledialog);
      buttonSend = new Button();
      buttonSend.Location = new System.Drawing.Point(120, 32);
      buttonSend.Text = "Send";
      buttonSend.Click += new System.EventHandler(OnSend);
      Controls.Add(buttonSend);
      listBox = new ListBox();
      listBox.Location = new System.Drawing.Point(40, 80);
      listBox.Size = new System.Drawing.Size(200, 100);
      listBox.SelectionMode = SelectionMode.One;
      listBox.SelectedIndexChanged += new System.EventHandler(OnSelection);
      //show list of valid com ports
      textBox = new TextBox();
      textBox.ReadOnly =  true;
      textBox.Multiline = false;
      textBox.Location = new System.Drawing.Point(40, 200);
      textBox.Size = new System.Drawing.Size(260, 20);
      Controls.Add(textBox);
      foreach (string s in SerialPort.GetPortNames()) {
       listBox.Items.Add(s);
      }
      if(listBox.Items.Count>0)
       listBox.SetSelected(0, true);
      Controls.Add(listBox);
      ResumeLayout(false);
    }
    
    [STAThread]
    public static void Main(string[] args)
    {
      Application.Run(new MainForm());
    }
    
    void OnClick(object sender, System.EventArgs e)
    {
        OpenFileDialog myFileDialog = new OpenFileDialog();
        myFileDialog.Filter = "All Files (*.*)|*.*";
        myFileDialog.Multiselect = false;
        myFileDialog.RestoreDirectory = false;
        myFileDialog.ShowDialog();

        string s = myFileDialog.FileName.Trim();
        if (s != string.Empty) {
         this.textBox.Text = s;
        }
        myFileDialog.Dispose();
        myFileDialog = null;
    }
    void OnSelection(object sender, System.EventArgs e)
    {
      textBox.Text = listBox.SelectedItem.ToString();
    }
    void OnSend(object sender, System.EventArgs e)
    {
      String str;
      SerialPort serialPort = new SerialPort(listBox.SelectedItem.ToString().Trim(), 38400, Parity.None, 8, StopBits.One);
      serialPort.Handshake = Handshake.None;
      serialPort.ReadTimeout = 800;
      serialPort.ReadTimeout = 800;
      serialPort.NewLine = "\r\n";
      serialPort.Open();
      serialPort.Write("@/\r");
      this.textBox.Text="--";
      try {
       str=serialPort.ReadLine();
       textBox.Text = str;
      }
      catch (TimeoutException) {
        textBox.Text = "Time-out!!!";
      }
      serialPort.Close();
    }
    
  }
}
