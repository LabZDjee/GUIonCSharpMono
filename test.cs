using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace testDjee
{
  public class MainForm : Form
  {
    private Button buttonFiledialog, buttonSend;
    private ListBox listBox;
    private TextBox textBox;
    public string aesKeyStr;
    public string aesIvStr;
    public MainForm()
    {
      SuspendLayout();
      ClientSize = new System.Drawing.Size(350, 250);
      MaximizeBox = false;
      Text = "Djee / Tests on Mono CS with Forms";
      FormBorderStyle = FormBorderStyle.FixedDialog;
      buttonFiledialog = new Button();
      buttonFiledialog.Location = new Point(40, 32);
      buttonFiledialog.Text = "Select";
      buttonFiledialog.Click += new EventHandler(OnFileDialogClick);
      Controls.Add(buttonFiledialog);
      buttonSend = new Button();
      buttonSend.Location = new Point(120, 32);
      buttonSend.Text = "Send";
      buttonSend.Click += new EventHandler(OnSendClick);
      Controls.Add(buttonSend);
      listBox = new ListBox();
      listBox.Location = new Point(40, 80);
      listBox.Size = new Size(200, 100);
      listBox.SelectionMode = SelectionMode.One;
      listBox.SelectedIndexChanged += new EventHandler(OnSelection);
      //show list of valid com ports
      textBox = new TextBox();
      textBox.ReadOnly =  true;
      textBox.Multiline = false;
      textBox.Location = new Point(40, 200);
      textBox.Size = new Size(260, 20);
      Controls.Add(textBox);
      foreach (string s in SerialPort.GetPortNames()) {
       listBox.Items.Add(s);
      }
      if(listBox.Items.Count>0)
       listBox.SetSelected(0, true);
      Controls.Add(listBox);
      ResumeLayout(false);
 //     UTF8Encoding utf8 = new UTF8Encoding();
      using (Aes aes = Aes.Create()) {
       aesKeyStr = ConvertHexStringToByteArrayToHexString(aes.Key);
       aesIvStr = ConvertHexStringToByteArrayToHexString(aes.IV);
       Console.WriteLine(String.Format("AES Key: {0}", aesKeyStr));
       Console.WriteLine(String.Format("AES Initialization Vector: {0}", aesIvStr));
      }
    }
    
    [STAThread]
    public static void Main(string[] args)
    {
      Application.Run(new MainForm());
    }
    
    void OnFileDialogClick(object sender, System.EventArgs e)
    {
        OpenFileDialog myFileDialog = new OpenFileDialog();
        myFileDialog.Filter = "All Files (*.*)|*.*";
        myFileDialog.Multiselect = false;
        myFileDialog.RestoreDirectory = false;
        myFileDialog.ShowDialog();

        string s = myFileDialog.FileName.Trim();
        if (s != string.Empty) {
         this.textBox.Text = s;
         try {
          string[] array = File.ReadAllLines(s);
          foreach (string line in array) {
           byte[] k = ConvertHexStringToByteArray(aesKeyStr);
           byte[] iv = ConvertHexStringToByteArray(aesIvStr);
           byte[] crypt = AesEncryptStringToBytesAes(line, k, iv);
           string decod = AesDecryptStringFromBytes(crypt, k, iv);
           Console.WriteLine(ConvertHexStringToByteArrayToHexString(crypt));
           Console.WriteLine(decod);
          }
         }
         catch (Exception excp) {
          Console.WriteLine("Error: {0}", excp.Message);
         } 
        }      
        myFileDialog.Dispose();
        myFileDialog = null;
    }
    void OnSelection(object sender, System.EventArgs e)
    {
      textBox.Text = listBox.SelectedItem.ToString();
    }
    void OnSendClick(object sender, System.EventArgs e)
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
    
   static byte[] ConvertHexStringToByteArray(string hexString)
   {
     if (hexString.Length % 2 != 0) {
        throw new ArgumentException(String.Format("The binary key cannot have an odd number of digits: {0}", hexString));
     }
     byte[] HexAsBytes = new byte[hexString.Length / 2];
     for (int index = 0; index < HexAsBytes.Length; index++) {
      string byteValue = hexString.Substring(index * 2, 2);
      HexAsBytes[index] = byte.Parse(byteValue, NumberStyles.HexNumber);
     }
     return HexAsBytes; 
   }
   static string ConvertHexStringToByteArrayToHexString(byte [] bArr)
   {
     return(BitConverter.ToString(bArr).Replace("-", string.Empty));
   }
   static byte[] AesEncryptStringToBytesAes(string plainText, byte[] key, byte[] initVector)
   {
    byte[] encrypted;
    // Create an Aes object with the specified key and IV
    using (Aes aesAlg = Aes.Create()) {
     aesAlg.Key = key;
     aesAlg.IV = initVector;
     // Create a decrytor to perform the stream transform
     ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
     // Create the streams used for encryption
     using (MemoryStream msEncrypt = new MemoryStream()) {
      using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)) {
       using (StreamWriter swEncrypt = new StreamWriter(csEncrypt)) {
        //Write all data to the stream
        swEncrypt.Write(plainText);
       }
       encrypted = msEncrypt.ToArray();
      }
     }
    }
    return encrypted;
   }
   static string AesDecryptStringFromBytes(byte[] cipherText, byte[] key, byte[] initVector)
   {
    // Declare the string used to hold the decrypted text
    string plaintext = null;
    // Create an Aes object with the specified key and IV
    using (Aes aesAlg = Aes.Create()) {
     aesAlg.Key = key;
     aesAlg.IV = initVector;
     // Create a decrytor to perform the stream transform
     ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
     // Create the streams used for decryption.
     using (MemoryStream msDecrypt = new MemoryStream(cipherText)) {
      using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read)) {
       using (StreamReader srDecrypt = new StreamReader(csDecrypt)) {
        // Read the decrypted bytes from the decrypting stream
        // and place them in a string
        plaintext = srDecrypt.ReadToEnd();
       }
      }
     }
    }
    return plaintext;
   }
  }
}
