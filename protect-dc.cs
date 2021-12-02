using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlForClasses;


// stuff related with gCAU based system, aka Protect DC
namespace ProtectDc
{
    // purely static class gathering general purpose methods
    public class PdcUtil
    {
        public static byte[] ConvertHexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException(string.Format("An hex string cannot have an odd number of digits: {0}", hexString));
            }
            byte[] HexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < HexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                try
                {
                    HexAsBytes[index] = byte.Parse(byteValue, NumberStyles.HexNumber);
                }
                catch
                {
                    throw new ArgumentException(string.Format("Not a valid hex string: {0}", hexString));
                }
            }
            return HexAsBytes;
        }
        public static string ConvertByteArrayToHexString(byte[] bArr)
        {
            return (BitConverter.ToString(bArr).Replace("-", string.Empty));
        }
        public static byte[] AesEncryptStringToBytesAes(string plainText, byte[] key, byte[] initVector)
        {
            byte[] encrypted;
            // Create an Aes object with the specified key and IV
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = initVector;
                // Create a decryptor to perform the stream transform
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
                // Create the streams used for encryption
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }
            return encrypted;
        }
        public static string AesDecryptStringFromBytes(byte[] cipherText, byte[] key, byte[] initVector)
        {
            // Declare the string used to hold the decrypted text
            string plaintext = null;
            // Create an Aes object with the specified key and IV
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = initVector;
                // Create a decryptor to perform the stream transform
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
            return plaintext;
        }
        // default string comparison case insensitive
        // return: true in case of equality
        public static bool StrCaseCmp(string s1, string s2)
        {
            return (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase));
        }
        // transform a CamelCase expression to a space-separated one
        public static string CamelCaseToSpaceSeparated(string stringInCamelCase)
        {
            // regex with a negative look behind "(?<!" to make sure uppercase is not preceded by any char
            return Regex.Replace(stringInCamelCase, "(?<!^)([A-Z])", " $0", RegexOptions.Compiled);
        }
        // returns a string from DateTime in a preferred format (YYYY/MM/DD HH:MM:SS)
        //  if parameter dt is null, current date/time is used
        // nbFields: if 3, only returns date, if 5, returns without seconds, 6 returns everything (from year to seconds)
        public static string GetTimestamp(DateTime? dt = null, int nbFields = 6)
        {
            DateTime dateTime = dt == null ? DateTime.Now : (DateTime)dt;
            if (nbFields <= 3)
            {
                return dateTime.ToString("yyyy/MM/dd");
            }
            if (nbFields <= 5)
            {
                return dateTime.ToString("yyyy/MM/dd HH:mm");
            }
            return dateTime.ToString("yyyy/MM/dd HH:mm:ss");
        }
    }

    /*
     * Definition of one attribute: name,
     *  position in the object definition, value, and boolean to indicate attribute is readonly
     */
    public struct AgcObjectAttribute
    {
        public string name;
        public ushort position; /* 1-based */
        public string value;
        public bool readOnly;
    }
    // definition of one patch object and consequently its patch attribute values
    public struct AgcObject
    {
        public string objectName;
        // attributes to be patched
        //  (including read-only attributes with a defined value)
        public AgcObjectAttribute[] attributes;
        public int totalNumberOfAttributes; // total number of attributes this object contains
        public string BuildLine(int attributePosition)
        {
            foreach (AgcObjectAttribute attr in attributes)
            {
                if (attr.position == attributePosition)
                {
                    return string.Format("{0}.{1}{2} = \"{3}\"", objectName, attr.readOnly ? "!" : "", attr.name, attr.value);
                }
            }
            throw new System.ArgumentOutOfRangeException();
        }
    }
    // values to characterize an ACG line type
    public enum AgcLineType
    {
        Unknown,         // matches no data
        ObjectAttribute, // matches object.[!]attribute = "value"
        Meta,            // matches meta data: $name = "value"
        MultiLineMeta,   // matches meta data with values (multi-line)
        Comment,         // matches a comment (starting with a hash '#' sign)
        Other            // matches generic definition: name = "value"
    }
    public struct AgcLineBreakdown
    {
        public AgcLineType type;
        public string name; // object name or meta data name or simply generic name od "#" if comment
                            // values[0] is attribute value of object, a meta or generic value, or what's in behind "#" for comment
                            // can be a list of strings in case of MultiLineMeta
        public List<string> values;
        public AgcObjectAttribute attribute; // only relevant if of type 'ObjectAttribute'
        public string contents; // the entire line which is broken down (the only field filled in case of Unknown type)
                                // a guess of the current session, a meta defined by $<section> = "Start" and before $<section> = "End"
                                // set to empty at the beginning and when $<section> = "End" is detected
        public string section;
        // one-based line number
        public int lineNumber;
    }
    /*
     * Definition and handling methods of an AGC file
     */
    public class AgcConfigurationFile
    {
        private static Regex __filenameSignature__;
        private static Regex __objectValueDef__;
        private static Regex __metaValueDef__;
        private static Regex __multiMetaValueDef__;
        private static Regex __normalValueDef__;
        private static Regex __endOfMultilineValueDef__;
        static AgcConfigurationFile()
        {
            // defines an acceptable filename
            __filenameSignature__ = new Regex(@"^.+\.agc$", RegexOptions.IgnoreCase);
            // captures object name, readonly attribute, attribute, and value
            __objectValueDef__ = new Regex(@"^([A-Z][A-Z0-9_]*)\.(!?)([A-Za-z0-9]+)\s*=\s*""(.*)"".*$");
            // captures name and value of a meta definition (meaning names starts with a dollar sign)
            __metaValueDef__ = new Regex(@"^(\$[A-Za-z0-9_-]+)\s*=\s*""(.*)"".*$");
            // captures name and value of an unclosed meta definition
            //  meaning names starts with a dollar sign and definition starts after a double quote
            // but does not finish with a closing double quote
            __multiMetaValueDef__ = new Regex(@"^(\$[A-Za-z0-9_-]+)\s*=\s*""([^""]*)$");
            // captures name and value in a broad sense
            __normalValueDef__ = new Regex(@"^([A-Za-z0-9_-]+)\s*=\s*""(.*)"".*$");
            // captures end of multi-line definition: some contents with a finishing double quote
            __endOfMultilineValueDef__ = new Regex(@"^(.*)""[ \t]*$");
        }
        public static bool CheckFileNamePattern(string filename) => __filenameSignature__.Match(filename.Trim()).Success;
        private string _fullFileName;
        // returns filename as provided to constructor
        public string FullFileName
        {
            get
            {
                return _fullFileName;
            }
        }
        private string _fileName;
        public string FileName
        {
            get
            {
                return _fileName;
            }
        }
        private List<string> _contents;
        // returns contents of the agc
        public List<string> Contents
        {
            get
            {
                return _contents;
            }
            set
            {
                _contents = value;
            }
        }
        private bool _isOkay;
        // returns true if structures could be populated without error
        public bool IsOkay
        {
            get
            {
                return _isOkay;
            }
        }
        private string _errorDetails;
        // returns error details in case IsOkay is false
        public string ErrorDetails
        {
            get
            {
                return _errorDetails;
            }
        }
        public AgcConfigurationFile(string fullFileName)
        {
            _fullFileName = fullFileName.Trim();
            _fileName = Path.GetFileName(_fullFileName);
            if (CheckFileNamePattern(_fullFileName))
            {
                _isOkay = true;
            }
            else
            {
                _isOkay = false;
                _errorDetails = "File extension is wrong";
                _contents = new List<string>();
                return;
            }
            try
            {
                string[] fileContents = File.ReadAllLines(_fullFileName, Encoding.Default);
                _isOkay = true;
                _contents = new List<string>(fileContents);
            }
            catch (Exception excp)
            {
                _isOkay = false;
                _errorDetails = string.Format("Error: {0}", excp.Message);
                _contents = new List<string>();
                return;
            }
        }
        // . enumerator which returns every line, broken down with its AgcLineType encapsulated into
        //   an AgcLineBreakdown struct
        // . multiple lines can be stored in values field in case of MultiLineMeta
        public System.Collections.Generic.IEnumerable<AgcLineBreakdown> BrokenDownList()
        {
            int i;
            ushort currentAttributePosition = 1;
            string previousObjectName = "<dunno!!!>";
            string currentSection = "";
            for (i = 0; i < _contents.Count; i++)
            {
                AgcLineBreakdown result = new AgcLineBreakdown();
                result.lineNumber = i + 1;
                result.values = new List<string>();
                string line = _contents[i];
                result.contents = line;
                Match mo = __objectValueDef__.Match(line);
                if (mo.Success)
                {
                    result.type = AgcLineType.ObjectAttribute;
                    result.name = mo.Groups[1].Captures[0].Value;
                    if (result.name != previousObjectName)
                    {
                        previousObjectName = result.name;
                        currentAttributePosition = 1;
                    }
                    result.values.Add(mo.Groups[4].Captures[0].Value);
                    result.attribute.name = mo.Groups[3].Captures[0].Value;
                    result.attribute.position = currentAttributePosition++;
                    result.attribute.readOnly = mo.Groups[2].Captures[0].Value == "!";
                    result.attribute.value = result.values[0];
                }
                else
                { // not object/value
                    result.attribute.name = result.attribute.value = string.Empty;
                    result.attribute.readOnly = false;
                    result.attribute.position = 0;
                    mo = __metaValueDef__.Match(line);
                    if (mo.Success)
                    {
                        string value = mo.Groups[2].Captures[0].Value;
                        result.type = AgcLineType.Meta;
                        result.name = mo.Groups[1].Captures[0].Value;
                        result.values.Add(value);
                        if (PdcUtil.StrCaseCmp(value, "Start"))
                        {
                            currentSection = result.name;
                        }
                        else if (PdcUtil.StrCaseCmp(value, "End"))
                        {
                            currentSection = string.Empty;
                        }
                    }
                    else
                    { // not meta def
                        mo = __multiMetaValueDef__.Match(line);
                        if (mo.Success)
                        {
                            int j;
                            result.type = AgcLineType.MultiLineMeta;
                            result.name = mo.Groups[1].Captures[0].Value;
                            result.values.Add(mo.Groups[2].Captures[0].Value);
                            for (j = i + 1; j < _contents.Count; j++)
                            {
                                line = _contents[j];
                                mo = __endOfMultilineValueDef__.Match(line);
                                if (mo.Success)
                                {
                                    result.values.Add(mo.Groups[1].Captures[0].Value);
                                    break;
                                }
                                result.values.Add(line);
                            } // end for loop
                            i = j;
                        }
                        else
                        { // not multi-line meta def
                            mo = __normalValueDef__.Match(line);
                            if (mo.Success)
                            {
                                result.type = AgcLineType.Other;
                                result.name = mo.Groups[1].Captures[0].Value;
                                result.values.Add(mo.Groups[2].Captures[0].Value);
                            }
                            else
                            { // not default value def
                                if (line.Length > 1 && line[0] == '#')
                                {
                                    result.type = AgcLineType.Comment;
                                    result.name = "#";
                                    result.values.Add(line.Substring(1));
                                }
                                else
                                { // not comment
                                    result.type = AgcLineType.Unknown;
                                    result.name = "";
                                    result.values = null;
                                } // end comment test
                            } // end normal value test
                        } // end multi-line meta test
                    } // end meta test
                } // end object/attribute test
                result.section = currentSection;
                yield return result;
            }
        }
        // return true if some calibration data are different from the default value (1,024)
        public bool containsNonDefaultCalibrationData()
        {
            if (!IsOkay)
                return false;
            foreach (AgcLineBreakdown agcLine in BrokenDownList())
            {
                if (PdcUtil.StrCaseCmp(agcLine.section, "$GCAUCalibrationData") &&
                   agcLine.type == AgcLineType.ObjectAttribute && agcLine.name == "CALIBR")
                {
                    if (agcLine.attribute.value != "1024")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

    }
    /*
     * class to deal with a language file
     */
    public class LanguageFile
    {
        private static Regex __filenameSignature__;
        private static Regex __recordStructure__;
        private static Regex __blankLine__;
        static LanguageFile()
        {
            __filenameSignature__ = new Regex(@"^LF_(\d+)_(\d+)_(.+)\.up\.txt$", RegexOptions.IgnoreCase);
            __recordStructure__ = new Regex(@"^(.+?)/(.+)\x16([0-9A-F]{2})");
            __blankLine__ = new Regex(@"^\s$");
        }
        public string FileName { get => _filename; }
        private string _filename;
        public string FullFileName { get => _fullFilename; }
        private string _fullFilename;
        public bool FilenamePatternIsOkay { get => _filePatternIsOkay; }
        private bool _filePatternIsOkay;
        public bool IsOkay { get => _isOkay; }
        private bool _isOkay;
        public string ErrorDetails { get => _errorDetails; }
        private string _errorDetails;
        public int Version { get => _version; }
        private int _version;
        public int SubVersion { get => _subVersion; }
        private int _subVersion;
        public string LanguageTag { get => _languageTag; }
        private string _languageTag;
        public LanguageFile(string FullFilename, bool verifyChecksums = true)
        {
            _fullFilename = FullFilename.Trim();
            _filename = Path.GetFileName(_fullFilename);
            records = new string[0];
            strippedRecords = new String[0];
            Match mo = __filenameSignature__.Match(_filename);
            _version = -1;
            _subVersion = -1;
            if (mo.Success)
            {
                _filePatternIsOkay = true;
                int.TryParse(mo.Groups[1].Captures[0].Value, out _version);
                int.TryParse(mo.Groups[2].Captures[0].Value, out _subVersion);
                _languageTag = mo.Groups[3].Captures[0].Value;
                try
                {
                    string[] fileContents = File.ReadAllLines(FullFilename, Encoding.Default);
                    List<string> recordList = new List<string>();
                    List<string> strippedRecordList = new List<string>();
                    for (int i = 0; i < fileContents.Length; i++)
                    {
                        string line = fileContents[i];
                        if (__blankLine__.Match(line).Success)
                        {
                            continue;
                        }
                        mo = __recordStructure__.Match(line);
                        if (mo.Success)
                        {
                            recordList.Add(line.Trim());
                            string prefix, contents, checksum;
                            prefix = mo.Groups[1].Captures[0].Value;
                            contents = mo.Groups[2].Captures[0].Value;
                            checksum = mo.Groups[3].Captures[0].Value;
                            if (verifyChecksums)
                            {
                                string composedContents = string.Format("{0}/{1}\x16", prefix, contents);
                                SpgUtil.ChecksumValue chk = SpgUtil.CalculateChecksum(composedContents);
                                if (chk.asHexString != checksum)
                                {
                                    _isOkay = false;
                                    _errorDetails = string.Format("wrong checksum at line {0} (should be {1}, not {2})", i + 1, chk.asHexString, checksum);
                                    return;
                                }
                                strippedRecordList.Add(contents);
                            }
                        }
                        else
                        {
                            _isOkay = false;
                            _errorDetails = string.Format("syntax error at line {0}", i + 1);
                            return;
                        }
                    }
                    records = recordList.ToArray();
                    strippedRecords = strippedRecordList.ToArray();
                    _isOkay = true;
                }
                catch (Exception excp)
                {
                    _isOkay = false;
                    _errorDetails = string.Format("I/O Error: {0}", excp.Message);
                    return;
                }

            }
            else
            {
                _isOkay = false;
                _errorDetails = "filename does not comply with language file pattern";
                _languageTag = "";
                _filePatternIsOkay = false;
            }
        }
        public string[] Records
        {
            get => records;
        }
        private string[] records;
        public string[] StrippedRecords
        {
            get => strippedRecords;
        }
        private string[] strippedRecords;

    }
    // file extension for patch files:
    //  encoded: agcp 
    //  decoded: agcp0
    // patch file structure:
    //  [Description]
    //  <textual description>
    //  [Data]
    //  <encoded or decoded data> 
    // data follows .agc configuration file structure:
    //  <obj>.[!]<attribute> = "<value>" when <value> is defined
    //  <obj>.[!]<attribute> when no <value> is defined
    //   all attributes must appear in the ordinal order
    public class AgcPatchFile
    {
        private static Regex __encodedFilenameSignature__;
        private static Regex __decodedFilenameSignature__;
        private static Regex __agcFilenameSignature__;
        private static Regex __objAttrRegex__;
        private static Regex __attrValRegex__;
        private static Regex __validUntilRegex__;
        private string _fullFileName;
        private string _fileName;
        // section tags
        public const string DescriptionSectionTag = "[Description]";
        public const string DataSectionTag = "[Data]";
        // returns filename as provided to constructor
        public string FullFileName
        {
            get
            {
                return _fullFileName;
            }
        }
        // return filename without its path (if any)
        public string FileName
        {
            get
            {
                return _fileName;
            }
        }
        private bool _isOkay;
        // returns true if structures could be populated without error
        public bool IsOkay
        {
            get
            {
                return _isOkay;
            }
        }
        private bool _isEncoded;
        // returns true if patch file data are encoded
        public bool IsEncoded
        {
            get
            {
                return _isEncoded;
            }
        }
        private bool _fromAgc;
        // returns true if patch file data are encoded
        public bool FromAgc
        {
            get
            {
                return _fromAgc;
            }
        }
        private bool _isOutDated;
        // return true if file is outdated
        public bool IsOutDated
        {
            get
            {
                return _isOutDated;
            }
        }
        private string _errorDetails;
        // returns error details in case IsOkay is false
        public string ErrorDetails
        {
            get
            {
                return _errorDetails;
            }
        }
        private bool _fullAttributeSetFlag;
        // returns whether we want every attributes from file even those without a value
        public bool FullAttributeFlag
        {
            get
            {
                return _fullAttributeSetFlag;
            }
        }
        private List<string> description;
        private string[] fileContents; // always decoded!
        private List<AgcObject> patchObjList;
        private int dataLineIndex;
        private static byte[] aesKey;
        private static byte[] aesInitialisationVector;
        // class constructor defines Regex objects
        static AgcPatchFile()
        {
            // defines an acceptable encoded filename
            __encodedFilenameSignature__ = new Regex(@"^.+\.agcp$", RegexOptions.IgnoreCase);
            // defines an acceptable decoded filename
            __decodedFilenameSignature__ = new Regex(@"^.+\.agcp0$", RegexOptions.IgnoreCase);
            // defines an acceptable agc filename
            __agcFilenameSignature__ = new Regex(@"^.+\.agc$", RegexOptions.IgnoreCase);
            // captures object name, readonly attribute, attribute and rest of string potentially containing a value
            __objAttrRegex__ = new Regex(@"^([A-Z][A-Z0-9_]*)\.(!?)([A-Za-z0-9]+)(.*)$");
            // captures value from last capture in regex above
            __attrValRegex__ = new Regex("^\\s*=\\s*\"(.*)\"");
            // defines a valid date after which agc file is no longer valid
            __validUntilRegex__ = new Regex("^\\$ValidUntil\\s*=\\s*\\\"(\\d{4})/(\\d{2})/(\\d{2})\\\"\\s*");
            // retrieves cryptographic secrets
            aesKey = PdcUtil.ConvertHexStringToByteArray(companySecretAes.SecretAES.Key);
            aesInitialisationVector = PdcUtil.ConvertHexStringToByteArray(hexString: companySecretAes.SecretAES.InitialisationVector);
        }
        // upon construction this object reads the patch file
        // decodes it if necessary and populates description strings and list of PatchObject
        //  getAllAttributeSet: builds list with all attributes even those bearing no value
        // this constructor also accepts regular configuration file (.agc)
        //  in this case, $notes metadata is copied to the [Description] section
        //  also, flag 'getAllAttributeSet' controls whether to inject (true, or not, false) all calibration data as a read-write object
        public AgcPatchFile(string fullFileName, bool getAllAttributeSet = false)
        {
            dataLineIndex = -1;
            fileContents = new string[0];
            patchObjList = new List<AgcObject>();
            description = new List<string>();
            _fullAttributeSetFlag = getAllAttributeSet;
            _fullFileName = fullFileName.Trim();
            _fileName = Path.GetFileName(_fullFileName);
            if (__encodedFilenameSignature__.Match(_fullFileName).Success)
            {
                _isOkay = true;
                _isEncoded = true;
                _fromAgc = false;
            }
            else if (__decodedFilenameSignature__.Match(_fullFileName).Success)
            {
                _isOkay = true;
                _isEncoded = false;
                _fromAgc = false;
            }
            else if (__agcFilenameSignature__.Match(_fullFileName).Success)
            {
                _isOkay = true;
                _isEncoded = false;
                _fromAgc = true;
            }
            else
            {
                _isOkay = false;
                _isEncoded = false;
                _errorDetails = "File extension is wrong";
                return;
            }
            string[] contents;
            if (_fromAgc)
            {
                _isEncoded = false;
                AgcConfigurationFile agcFile = new AgcConfigurationFile(_fullFileName);
                if (agcFile.IsOkay == false)
                {
                    _isOkay = false;
                    _errorDetails = agcFile.ErrorDetails;
                    return;
                }
                List<string> description = new List<string>();
                List<string> data = new List<string>();
                string value;
                string previousObject = "";
                description.Add(DescriptionSectionTag);
                data.Add(DataSectionTag);
                IEnumerable<AgcLineBreakdown> brokenDownList = agcFile.BrokenDownList();
                foreach (AgcLineBreakdown agcLine in brokenDownList)
                {
                    if (PdcUtil.StrCaseCmp(agcLine.section, "$GCAUConfigurationData"))
                    {
                        if (PdcUtil.StrCaseCmp(agcLine.name, "$Notes"))
                        {
                            for (int i = 0; i < agcLine.values.Count; i++)
                            {
                                value = agcLine.values[i].TrimEnd();
                                if (value.Length > 0)
                                    description.Add(value);
                            }
                        }
                        if (agcLine.type == AgcLineType.ObjectAttribute)
                        {
                            if (previousObject.Length > 0 && PdcUtil.StrCaseCmp(previousObject, agcLine.name) == false)
                            {
                                data.Add("");
                            }
                            value = $"{agcLine.name}.{(agcLine.attribute.readOnly ? "!" : "")}{agcLine.attribute.name} = \"{agcLine.attribute.value}\"";
                            data.Add(value);
                            previousObject = agcLine.name;
                        }
                    }
                    if (getAllAttributeSet &&
                      PdcUtil.StrCaseCmp(agcLine.section, "$GCAUCalibrationData") &&
                      agcLine.type == AgcLineType.ObjectAttribute &&
                      agcLine.name == "CALIBR")
                    {
                        if (previousObject.Length > 0 && PdcUtil.StrCaseCmp(previousObject, agcLine.name) == false)
                        {
                            data.Add("");
                        }
                        value = $"{agcLine.name}.{agcLine.attribute.name} = \"{agcLine.attribute.value}\"";
                        data.Add(value);
                        previousObject = agcLine.name;
                    }
                }
                fileContents = new string[data.Count + description.Count];
                description.CopyTo(fileContents);
                data.CopyTo(fileContents, description.Count);
            }
            else
            {
                try
                {
                    contents = File.ReadAllLines(_fullFileName);
                    _isOkay = true;
                }
                catch (Exception excp)
                {
                    _isOkay = false;
                    _errorDetails = string.Format("Error: {0}", excp.Message);
                    return;
                }
                if (_isEncoded)
                {
                    bool insideData = false;
                    int i;
                    fileContents = new string[contents.Length];
                    for (i = 0; i < contents.Length; i++)
                    {
                        if (insideData)
                        {
                            byte[] crypt = PdcUtil.ConvertHexStringToByteArray(contents[i]);
                            fileContents[i] = PdcUtil.AesDecryptStringFromBytes(crypt, aesKey, aesInitialisationVector);
                        }
                        else
                        {
                            fileContents[i] = contents[i];
                            if (PdcUtil.StrCaseCmp(contents[i], DataSectionTag))
                            {
                                insideData = true;
                            }
                        }
                    }
                }
                else
                {
                    fileContents = contents;
                }
            }
            byte stage = 0;
            string currentObjectInstance = "--";
            byte currentAttributePosition = 1;
            int objListIndex = -1;
            int lineIndex;
            string s;
            List<AgcObjectAttribute> patchObjAttrList = null;
            List<List<AgcObjectAttribute>> patchObjAttrListofList = new List<List<AgcObjectAttribute>>();
            for (lineIndex = 0; lineIndex < fileContents.Length; lineIndex++)
            {
                s = fileContents[lineIndex];
                if (stage == 1)
                {
                    if (PdcUtil.StrCaseCmp(s, DataSectionTag))
                    {
                        stage = 2;
                        if (lineIndex + 1 < fileContents.Length)
                            dataLineIndex = lineIndex + 1;
                    }
                    else
                    {
                        description.Add(s);
                    }
                }
                else if (stage == 2)
                {
                    Match mo = __validUntilRegex__.Match(s);
                    if (mo.Success)
                    {
                        try
                        {
                            int y, m, d;
                            y = int.Parse(mo.Groups[1].Captures[0].Value);
                            m = int.Parse(mo.Groups[2].Captures[0].Value);
                            d = int.Parse(mo.Groups[3].Captures[0].Value);
                            DateTime validityLimit = new DateTime(y, m, d, 23, 59, 59);
                            if (DateTime.Now > validityLimit)
                            {
                                _isOutDated = true;
                            }
                        }
                        catch { }
                    }
                    bool readOnly;
                    string obj, attribute, value;
                    int foundLevel = ParseAgcLine(s, out obj, out attribute, out readOnly, out value);
                    if (foundLevel > 0)
                    {
                        if (obj != currentObjectInstance)
                        {
                            currentObjectInstance = obj;
                            currentAttributePosition = 1;
                            patchObjAttrList = new List<AgcObjectAttribute>();
                            patchObjList.Add(new AgcObject
                            {
                                objectName = currentObjectInstance
                            });
                            objListIndex++;
                            patchObjAttrList = new List<AgcObjectAttribute>();
                            patchObjAttrListofList.Add(patchObjAttrList);
                        }
                        if (foundLevel > 1 || _fullAttributeSetFlag)
                        {
                            AgcObjectAttribute patchObjAttr = new AgcObjectAttribute
                            {
                                name = attribute,
                                position = currentAttributePosition,
                                value = foundLevel > 1 ? value : null,
                                readOnly = readOnly
                            };
                            patchObjAttrList.Add(patchObjAttr);
                        }
                        AgcObject po = patchObjList[objListIndex];
                        po.totalNumberOfAttributes = currentAttributePosition;
                        patchObjList[objListIndex] = po;
                        currentAttributePosition++;
                    }
                }
                else
                {
                    if (PdcUtil.StrCaseCmp(s, DescriptionSectionTag))
                        stage = 1;
                }
            }
            if (stage != 2)
            {
                _isOkay = false;
                _errorDetails = "Inconsistent file";
                return;
            }
            for (int i = 0; i < patchObjList.Count; i++)
            {
                AgcObject p = patchObjList[i];
                p.attributes = patchObjAttrListofList[i].ToArray();
                patchObjList[i] = p;
            }
            _errorDetails = "No error";
        }
        public string[] GetDecodedContents()
        {
            return fileContents;
        }
        public static string[] GetEncodedContents(string[] externalContents)
        {
            string[] encoded = new string[externalContents.Length];
            int i;
            bool dataArea = false;
            for (i = 0; i < externalContents.Length; i++)
            {
                if (dataArea)
                {
                    byte[] crypt = PdcUtil.AesEncryptStringToBytesAes(externalContents[i], aesKey, aesInitialisationVector);
                    encoded[i] = PdcUtil.ConvertByteArrayToHexString(crypt);
                }
                else
                {
                    encoded[i] = externalContents[i];
                    if (PdcUtil.StrCaseCmp(encoded[i], DataSectionTag))
                        dataArea = true;
                }
            }
            return encoded;
        }
        public string[] GetEncodedContents()
        {
            return GetEncodedContents(fileContents);
        }
        // returns populated [description]
        public string[] GetDescription()
        {
            return description.ToArray();
        }
        // return populated list of patch objects containing their attributes to be patched
        public AgcObject[] GetPatchObjects()
        {
            return (patchObjList.ToArray());
        }
        // return populated list of patch objects containing their attributes to be patched
        // filtered out by class restrictions
        public AgcObject[] GetPatchObjects(GcauClassRestrictions classRestrictions)
        {
            if (classRestrictions.TargetClassVersion >= 0)
            {
                List<AgcObject> filteredPatchObjList = new List<AgcObject>();
                foreach (AgcObject obj in patchObjList)
                {
                    try
                    {
                        YamlClassesProcessor.ClassInstanceBreakdown instance = YamlClassesProcessor.SplitObjectName(obj.objectName);
                        ClassDefs classDefs = classRestrictions.ClassHash[instance.className];
                        if (classDefs.SelectedIndex() >= 0) // else class not defined: ignore
                        {
                            GcauClassVersionDef classDef = classDefs.ClassDefinitions[classDefs.SelectedIndex()];
                            if (classDef.Instances == 0 || instance.instance <= classDef.Instances)
                            {
                                List<AgcObjectAttribute> attrList = new List<AgcObjectAttribute>();
                                foreach (AgcObjectAttribute attribute in obj.attributes)
                                {
                                    if (attribute.position <= classDef.CfgAttributes)
                                    {
                                        if (obj.objectName == "SYSVAR" && attribute.position == 18) // SYSVAR.EventEnable
                                        {
                                            // manages the tricky case of SYSVAR.EventEnable which depends on the number of EVT's
                                            AgcObjectAttribute trimmedAttribute = attribute;
                                            ClassDefs evtClassDefs = classRestrictions.ClassHash["EVT"];
                                            GcauClassVersionDef evtClassDef = evtClassDefs.ClassDefinitions[evtClassDefs.SelectedIndex()];
                                            int length = evtClassDef.Instances / 4;
                                            int delta = attribute.value.Length - length;
                                            if (delta > 0)
                                            {
                                                trimmedAttribute.value = attribute.value.Substring(delta, length);
                                            }
                                            else if (delta < 0)
                                            {
                                                trimmedAttribute.value = attribute.value.PadLeft(length, '0');
                                            }
                                            attrList.Add(trimmedAttribute);
                                        }
                                        else
                                        {
                                            attrList.Add(attribute);
                                        }
                                    }
                                }
                                if (attrList.Count > 0)
                                {
                                    AgcObject filteredObj = obj;
                                    filteredObj.objectName = obj.objectName;
                                    filteredObj.totalNumberOfAttributes = attrList.Count;
                                    filteredObj.attributes = attrList.ToArray();
                                    filteredPatchObjList.Add(filteredObj);
                                }
                            }
                        }
                    }
                    catch // in case of inconsistency, we prefer to ignore why it went wrong. TODO: be more analytic
                    {
                        filteredPatchObjList.Add(obj);
                    }
                }
                return (filteredPatchObjList.ToArray());
            }
            else
            {
                return (patchObjList.ToArray());
            }
        }

        // Simple utility using internal regex's to extract fields from a configuration line of type
        //  <obj>.[!]<attribute> = "<value>"
        // return:
        //  0 no match at all (strings returned empty)
        //  1 object, attribute, read-only found but no value (value return as empty string)
        //  2 all four out parameters properly filled
        public static int ParseAgcLine(string line, out string obj, out string attribute, out bool readOnly, out string value)
        {
            Match mo = __objAttrRegex__.Match(line);
            if (mo.Success)
            {
                obj = mo.Groups[1].Captures[0].Value;
                readOnly = mo.Groups[2].Captures[0].Value == "!";
                attribute = mo.Groups[3].Captures[0].Value;
                Match mv = __attrValRegex__.Match(mo.Groups[4].Captures[0].Value);
                if (mv.Success)
                {
                    value = mv.Groups[1].Captures[0].Value;
                    return 2;
                };
                value = string.Empty;
                return 1;
            }
            obj = attribute = value = string.Empty;
            readOnly = false;
            return 0;
        }
        // takes a data line from a patch file and apply it to an AGC file
        // return false if cannot be done
        public static bool PatchLineToAgcCfgFile(string patchDataLine, List<string> agcCfgFile)
        {
            string obj, attribute, value;
            bool readOnly;
            int r = ParseAgcLine(patchDataLine, out obj, out attribute, out readOnly, out value);
            if (r != 2)
                return false;
            return true;
        }
    }
    // purely static class containing methods for dealing with the SPG frames (gCAU protocol)
    public class SpgUtil
    {
        private static Regex __RecvFrameRegex__ = null;
        private static Regex __PasswordReplyRegex__ = null;
        public static string LineTerminator()
        {
            return ("\r\n\x06");
        }
        public static Regex GetRecvFrameRegex()
        {
            if (__RecvFrameRegex__ == null)
            {
                // regular expression pattern for a SPG reveived frame
                //  /{verb}/{arg1}/...{argN}[/]{SYN}{checksum:2-hex-digits}
                //  groups/captures:
                //   captures in group 1 store {verb} and {args}
                //   capture in group 2 stores {checksum:2-hex-digits}
                //  note: {verb} and {args} can contain a slash if preceded by a back-slash
                //  reminder: (?: ) groups without capturing
                string SPGRecvFramePattern = @"^(?:/((?:(?:\\/|[^/]))*))+/\x16([0-9A-F]{2})";
                __RecvFrameRegex__ = new Regex(SPGRecvFramePattern, RegexOptions.Compiled);
            }
            return __RecvFrameRegex__;
        }
        // pattern for a reply to a checksum field
        public static Regex GetPasswordReplyRegex()
        {
            if (__PasswordReplyRegex__ == null)
            {
                // a line filled with stars (asterisks: *)
                string PasswordReplyPattern = @"^\*+$";
                __PasswordReplyRegex__ = new Regex(PasswordReplyPattern, RegexOptions.Compiled);
            }
            return __PasswordReplyRegex__;
        }
        // data: different facets of a checksum
        public struct ChecksumValue
        {
            public byte asByte;
            public string asHexString;
            public override string ToString()
            {
                return (string.Format("{{asByte = {0}, asHexString = {1}}}", asByte, asHexString));
            }
        }
        // calculate checksum of an SPG data frame (defined as covering everything up to and including SYN (0x16) character
        public static ChecksumValue CalculateChecksum(string frame)
        {
            const byte SYN = 0x16;
            byte[] frameBytes = Encoding.ASCII.GetBytes(frame);
            byte result = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                result += frameBytes[i];
                if (frameBytes[i] == SYN)
                    break;
            }
            result = (byte)(~result + 1);
            return new ChecksumValue() { asByte = result, asHexString = result.ToString("X2") };
        }
        public enum FrameError
        {
            Ok = 1,
            UndefinedError = -1,
            Timeout = -5,
            WrongFrameFormat = -10,
            WrongCheckum = -15,
            ReplyTooShort = -20,
            ReplyTooLong = -25,
            WrongValueFormat = -30,
            RepliedSyntaxError = -40,
            RepliedUnknowVerb = -41,
            RepliedUnknownObject = -42,
            RepliedBadAttributeAddress = -43,
            RepliedBadAttributeValue = -44,
            RepliedBadAttributeRange = -45,
            RepliedAccessLevelTooLow = -46,
            RepliedNotExecuted = -47,
            RepliedLevelLocked = -48,
            RepliedNotLogged = -49,
            RepliedGenericUndefinedError = -50,
            RepliedBufferOverflow = -51,
            RepliedProtocolTemporaryDisconnected = -52,
            WrongVerbInReply = -60,
            WrongObjectInReply = -65,
            ReplyFieldDoesNotMatchRequestField = -100
        }
        // data: break down of an SPG received data frame
        public struct ParsedReceivedFrame
        {
            public bool valid;
            public FrameError troubleReason; // when negative, gives additional data of reason why it's not valid
            public string[] values;
            public ChecksumValue checksum;
            public override string ToString()
            {
                if (valid == false)
                {
                    return (string.Format("{{valid: {0}, troubleReason: {1}, values = {{{1}}}, checksum = {2}}}",
                      valid, troubleReason, string.Join(", ", values), checksum));

                }
                return (string.Format("{{valid: {0}, values = {{{1}}}, checksum = {2}}}",
                  valid, string.Join(", ", values), checksum));
            }
        }
        // parse a received SPG data frame and return its breakdown
        public static ParsedReceivedFrame ParseReceivedFrame(string frame, bool verifyChecksum = true)
        {
            Regex rx = GetRecvFrameRegex();
            Match match = rx.Match(frame);
            if (!match.Success)
                return new ParsedReceivedFrame()
                {
                    valid = false,
                    troubleReason = FrameError.WrongFrameFormat,
                    values = new string[0],
                    checksum = new ChecksumValue() { asByte = 0, asHexString = "00" }
                };
            List<string> values = new List<string>();
            foreach (Capture capture in match.Groups[1].Captures)
            {
                values.Add(capture.Value);
            }
            string checksumStr = match.Groups[2].Captures[0].Value;
            ChecksumValue checksum = new ChecksumValue() { asByte = (byte)Convert.ToUInt32(checksumStr, 16), asHexString = checksumStr };
            ChecksumValue calculatedCheckSum = CalculateChecksum(frame);
            bool valid = verifyChecksum == false || checksum.asByte == calculatedCheckSum.asByte;
            return new ParsedReceivedFrame()
            {
                valid = valid,
                troubleReason = valid ? FrameError.Ok : FrameError.WrongCheckum,
                values = values.ToArray(),
                checksum = checksum
            };
        }
        // check if parsed reply reported an explicit error (#ERROR) and if so, returns it
        // otherwise returns what parsedFrame.troubleReason
        public static FrameError TestIfReplyIsAnExplicitError(ParsedReceivedFrame parsedFrame)
        {
            if (parsedFrame.valid == false || parsedFrame.values.Length < 2 || parsedFrame.values[0] != "#ERROR")
                return parsedFrame.troubleReason;
            switch (parsedFrame.values[1])
            {
                case "SE":
                    return FrameError.RepliedSyntaxError;
                case "UV":
                    return FrameError.RepliedUnknowVerb;
                case "UO":
                    return FrameError.RepliedUnknownObject;
                case "BA":
                    return FrameError.RepliedBadAttributeAddress;
                case "BV":
                    return FrameError.RepliedBadAttributeValue;
                case "BR":
                    return FrameError.RepliedBadAttributeRange;
                case "AL":
                    return FrameError.RepliedAccessLevelTooLow;
                case "NE":
                    return FrameError.RepliedNotExecuted;
                case "LL":
                    return FrameError.RepliedLevelLocked;
                case "NL":
                    return FrameError.RepliedNotLogged;
                case "UE":
                    return FrameError.RepliedGenericUndefinedError;
                case "BO":
                    return FrameError.RepliedBufferOverflow;
                case "DC":
                    return FrameError.RepliedProtocolTemporaryDisconnected;
                default:
                    return FrameError.UndefinedError;
            }
        }
        // encode SPG values by escaping with backslashes
        public static string EncodeValue(string value)
        {
            const char nil = (char)0;
            const char del = (char)0x7f;
            string result = "";
            foreach (char c0 in value)
            {
                char c = (char)((int)c0 % 128);
                switch (c)
                {
                    case '/':
                        result += @"\/";
                        break;
                    case ':':
                        result += @"\:";
                        break;
                    case nil:
                        result += @"\0";
                        break;
                    case '\a':
                        result += @"\a";
                        break;
                    case '\b':
                        result += @"\b";
                        break;
                    case '\f':
                        result += @"\f";
                        break;
                    case '\n':
                        result += @"\n";
                        break;
                    case '\r':
                        result += @"\r";
                        break;
                    case '\t':
                        result += @"\t";
                        break;
                    case '\v':
                        result += @"\v";
                        break;
                    case '\\':
                        result += @"\\";
                        break;
                    case '~':
                        result += @"\~";
                        break;
                    case '$':
                        result += @"\$";
                        break;
                    case '&':
                        result += @"\&";
                        break;
                    case '@':
                        result += @"\@";
                        break;
                    case '_':
                        result += @"\_";
                        break;
                    case del:
                        result += @"\d";
                        break;
                    default:
                        if (c >= 0x20 && c <= 0x7f)
                            result += c;
                        else
                            result += @"\c" + (char)(c + 0x40);
                        break;
                }
            }
            return result;
        }
        // decode SPG values by de-escaping backslashes
        public static string DecodeValue(string encoded)
        {
            string result = string.Empty;
            bool escaped = false;
            bool doubleEscaped = false;
            foreach (char c in encoded)
            {
                if (doubleEscaped)
                {
                    if (c >= 0x40 && c <= 0x5f)
                        result += (char)(c - 0x40);
                    escaped = false;
                    doubleEscaped = false;
                }
                else if (escaped)
                {
                    switch (c)
                    {
                        case '\\':
                            result += '\\';
                            break;
                        case '0':
                            result += (char)0;
                            break;
                        case 'a':
                            result += "\a";
                            break;
                        case 'b':
                            result += "\b";
                            break;
                        case 'c':
                            doubleEscaped = true;
                            break;
                        case 'd':
                            result += (char)0x7f;
                            break;
                        case 'f':
                            result += "\f";
                            break;
                        case 'n':
                            result += "\n";
                            break;
                        case 'r':
                            result += "\r";
                            break;
                        case 't':
                            result += "\t";
                            break;
                        case 'v':
                            result += "\v";
                            break;
                        case '<':
                            result += (char)0x1c;
                            break;
                        case '?':
                            result += (char)0x1f;
                            break;
                        default:
                            result += c;
                            break;
                    }
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else
                {
                    result += c;
                }
            }
            return result;
        }
        // adds 0x16, checksum, and CR to an SPG data frame
        public static string CloseSpgCommand(string command)
        {
            string closedCommand = string.Format("{0}\x16", command);
            return string.Format("{0}{1}\r", closedCommand, CalculateChecksum(closedCommand).asHexString);
        }
        // compute a backdoor challenge reply code
        // return string containing three computed digits or an empty string in case of inconsistency
        public static string ComputeBDChallengeReply(string challenge)
        {
            if (challenge.Length == 3 && char.IsDigit(challenge[1]) && char.IsDigit(challenge[2]))
            {
                int a = int.Parse(challenge[0].ToString()),
                    b = int.Parse(challenge[1].ToString()),
                    c = int.Parse(challenge[2].ToString());
                int z = (b + c + 2) % 10;
                int y = (z + b + 5) % 10;
                int x = (a + y + 7) % 10;
                return string.Format("{0}{1}{2}", x, y, z);
            }
            return string.Empty;
        }
        // compares a 'replyField' against was what asked to write ('requestField')
        // beware of consistency: 'requestField' and 'replyField' should both be encoded (escaped) or decoded (de-escaped)
        public static bool CompareReplyField(string requestField, string replyField)
        {
            const double epsilon = 1e-4; // for near equality in floats
            Regex rx = GetPasswordReplyRegex(); // a regex to match a string with only asteriks, something like: "^\*+$"
            Match match = rx.Match(replyField); // asterisks in reply takes all
            if (match.Success)
            {
                return true;
            }
            if (requestField == replyField)
            { // equality is okay
                return true;
            }
            // tests if equation (elements which are space separated)
            string[] requests = requestField.Split(' ');
            string[] replys = replyField.Split(' ');
            if (requests.Length == replys.Length)
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    if (requests[i] == replys[i])
                    { // equality in equation test is okay
                        continue;
                    }
                    // tests if float constants are close enough
                    if (double.TryParse(requests[i], out double f1) && double.TryParse(replys[i], out double f2))
                    {
                        if (f1 == 0.0 && Math.Abs(f2) < epsilon || Math.Abs((f1 - f2) / f1) < epsilon)
                        {
                            continue;
                        }
                    }
                    return false;
                } // end for
                return true;
            }
            return false;
        }
        // helper to send an AgcObject as encoded commands called "Chuncks"
        // reason for chunks stems from limited length of an SPG command
        public class WriteChunks
        {
            // what is a chunk of data to write and then compare with the reply
            public class Chunk
            {
                public List<AgcObjectAttribute> Attributes; // involved attributes
                public string Command; // SPG command to send
                public WriteChunks Parent
                {
                    get;
                }
                public string Reply; // for diagnostic only
                public Chunk(string prefix, WriteChunks parent = null)
                {
                    Attributes = new List<AgcObjectAttribute>();
                    Command = prefix;
                    Parent = parent;
                    Reply = string.Empty;
                }
            }
            public const int MaxLength = 100; // max accepted length of a command to write
                                              // external read-only accessors
            public List<Chunk> Chunks
            {
                get;
            } // result
            public AgcObject AgcObject
            {
                get;
            }
            public int SlaveNumber
            {
                get;
            } /* -1: invalid / not defined */
            public int Channel
            {
                get;
            }     /* -1: invalid / not defined */
            public string Verb
            {
                get;
            }
            public string Prefix
            {
                get;
            }
            // take an AgcObject, a verb (e.g.: "WCFG"), an optional (positive) slaveNumber, an optional (positive) channel
            // and builds Chunks
            public WriteChunks(AgcObject agcObj, string verb, int slaveNumber = -1, int channel = -1)
            {
                AgcObject = agcObj;
                SlaveNumber = slaveNumber;
                Channel = channel;
                Verb = verb;
                Prefix = string.Format("@{0}{1}",
                  SlaveNumber < 0 ? "" : string.Format("{0}", SlaveNumber),
                  Channel < 0 ? "" : string.Format("&{0}", Channel));
                Chunks = new List<Chunk>();
                Chunk chunk = new Chunk(string.Format("{0}/{1}/{2}", Prefix, Verb, agcObj.objectName), this);
                foreach (AgcObjectAttribute attr in AgcObject.attributes)
                {
                    const int suffixLength = 4;
                    if (attr.readOnly == false)
                    {
                        string value = EncodeValue(attr.value);
                        string addition = string.Format("/{0}:{1}", attr.position, value);
                        if (chunk.Command.Length + addition.Length + suffixLength > MaxLength)
                        {
                            chunk.Command = CloseSpgCommand(chunk.Command);
                            Chunks.Add(chunk);
                            chunk = new Chunk(string.Format("{0}/{1}/{2}", Prefix, Verb, agcObj.objectName), this);
                        }
                        chunk.Command += addition;
                        chunk.Attributes.Add(attr);
                    }
                }
                chunk.Command = CloseSpgCommand(chunk.Command);
                Chunks.Add(chunk);
            }
            public static FrameError CheckReply(string reply, Chunk chunk)
            {
                ParsedReceivedFrame rcvFrame = ParseReceivedFrame(reply);
                chunk.Reply = reply;
                if (rcvFrame.valid)
                {
                    string[] fields = rcvFrame.values;
                    if (chunk.Parent != null)
                    {
                        if (fields.Length < 2)
                        {
                            return FrameError.ReplyTooShort;
                        }
                        if (fields[0] != chunk.Parent.Verb)
                        {
                            return FrameError.WrongVerbInReply;
                        }
                        if (fields[1] != chunk.Parent.AgcObject.objectName)
                        {
                            return FrameError.WrongObjectInReply;
                        }
                    }
                    foreach (AgcObjectAttribute attr in chunk.Attributes)
                    {
                        if (attr.position + 1 >= fields.Length)
                        {
                            return FrameError.ReplyTooShort;
                        }
                        if (CompareReplyField(attr.value, DecodeValue(fields[attr.position + 1])) == false)
                        {
                            return FrameError.ReplyFieldDoesNotMatchRequestField;
                        }
                    }
                }
                else
                {
                    return rcvFrame.troubleReason;
                }
                return FrameError.Ok;
            }

            // debug
            public override string ToString()
            {
                string result = string.Format("Chunks for {0} with verb {1}:\n", AgcObject.objectName, Verb);
                if (Chunks.Count < 1)
                {
                    return result + " <Empty>";
                }
                int index = 1;
                foreach (Chunk chunk in Chunks)
                {
                    result += string.Format(" Chunk #{0}:\n", index);
                    if (chunk.Attributes.Count < 1)
                    {
                        result += "<no positions>\n";
                    }
                    else
                    {
                        result += "  position list: ";
                        foreach (AgcObjectAttribute attr in chunk.Attributes)
                        {
                            result += string.Format("{0} ", attr.position);
                        }
                        result = result.TrimEnd() + "\n";
                    }
                    result += string.Format("  command: {0}", chunk.Command);
                    index++;
                }
                return result.TrimEnd();
            }
        }
        // outcome of an asynchronous request
        public class ReplyData
        {
            public bool Error
            {
                get;
            } // if false, unknown error, overrides other fields below
            public bool TimedOut
            {
                get;
            } // if false, timeout error, overrides other fields below
            public string Reply
            {
                get;
            }
            public ReplyData(bool error = false, bool timedOut = false, string reply = "")
            {
                Error = error;
                TimedOut = timedOut;
                Reply = reply;
            }
        }
        // delegate to a task which returns outcome of an asynchronous request
        public delegate Task<ReplyData> RequestReplyDelegate(string request);
        // what any class should implement to satisfy a request-reply asynchronous round-trip
        public interface IRequestReply
        {
            Task<ReplyData> RequestReply(string request);
        }
        // powerful login procedure...
        public static async Task<bool> BDLogin(RequestReplyDelegate reqRep, bool version1 = false)
        {
            ReplyData reply = await reqRep(CloseSpgCommand("@&1/BKDOOR"));
            if (reply.Error == false && reply.TimedOut == false)
            {
                ParsedReceivedFrame prf = ParseReceivedFrame(reply.Reply);
                string challengeReply;
                if (prf.valid && prf.values.Length == 2 && prf.values[0] == "BKDOOR"
                  && (challengeReply = ComputeBDChallengeReply(prf.values[1])) != string.Empty)
                {
                    reply = await reqRep(CloseSpgCommand(string.Format("@/LOGIN/2/{0}{1}", challengeReply, version1 ? "" : "/50")));
                    if (reply.Error == false && reply.TimedOut == false)
                    {
                        prf = ParseReceivedFrame(reply.Reply);
                        if (prf.valid && prf.values.Length == 2 && prf.values[0] == "LOGIN" && prf.values[1] == "OK")
                            return true;
                    }
                }
            }
            return false;
        }
        // write a chunk and returns a reply verification report
        public static async Task<FrameError> WriteChunk(RequestReplyDelegate reqRep, WriteChunks.Chunk chunk)
        {
            ReplyData reply = await reqRep(chunk.Command);
            if (reply.TimedOut)
            {
                return FrameError.Timeout;
            }
            if (reply.Error)
            {
                return FrameError.UndefinedError;
            }
            chunk.Reply = reply.Reply;
            ParsedReceivedFrame parsedFrame = ParseReceivedFrame(reply.Reply);
            if (parsedFrame.valid == false)
            {
                return FrameError.WrongFrameFormat;
            }
            FrameError frameError = TestIfReplyIsAnExplicitError(parsedFrame);
            if (frameError != FrameError.Ok)
            {
                return frameError;
            }
            frameError = WriteChunks.CheckReply(reply.Reply, chunk);
            return frameError;
        }
        // send request 'fullRequest' and returns reply
        // 'fullRequest' is sent verbatim so prefix, checksum if any, end of frame should be embedded
        public static async Task<ParsedReceivedFrame> RequestReply(RequestReplyDelegate reqRep, string fullRequest)
        {
            ParsedReceivedFrame parsedFrame = new ParsedReceivedFrame() { valid = false };
            ReplyData reply = await reqRep(fullRequest);
            if (reply.TimedOut)
            {
                parsedFrame.troubleReason = FrameError.Timeout;
            }
            else if (reply.Error)
            {
                parsedFrame.troubleReason = FrameError.UndefinedError;
            }
            else
            {
                parsedFrame = ParseReceivedFrame(reply.Reply);
            }
            return parsedFrame;
        }
    }
    // utility for communications over serial port
    public class CommUtil
    {
        public static int[] possibleBaudrates = new int[] { 38400, 19200, 9600, 4800, 2400, 1200, 115200 };
        // descriptor of what a gCAU answers at a given port and baudrate
        public struct CommDesc
        {
            public string CommPort; // empty means undefined / not found
            public int Baudrate;
            public int SlaveNumber;
            public string ControllerSwVersion;
            public string RegulatorSwVersion;
            public int LanguageSchema;
            public int ObjectSchema;
            public string SerialNumber;
            public string LocalLanguage;
            public int ModbusTableVersion;
            public string SiteId;
            public string ProjectReference;
        }
        // request-reply asynchronous dialog on a given serial port
        public class RequestReplyHandler : SpgUtil.IRequestReply
        {
            public SerialPort SerialPort
            {
                get;
            }
            public int TimeOutInMs
            {
                get;
            }
            public SpgUtil.RequestReplyDelegate RequestReplyDelegate
            {
                get;
            }
            public RequestReplyHandler(SerialPort serialPort, int timeOutInMs = 2000)
            {
                SerialPort = serialPort;
                TimeOutInMs = timeOutInMs;
                if (SerialPort != null)
                {
                    SerialPort.ReadTimeout = TimeOutInMs;
                }
                RequestReplyDelegate = RequestReply;
            }
            public async Task<SpgUtil.ReplyData> RequestReply(string request)
            {
                if (SerialPort == null)
                {
                    return new SpgUtil.ReplyData(error: true);
                }
                try
                {
                    SerialPort.Write(request);
                }
                catch (InvalidOperationException)
                {
                    return new SpgUtil.ReplyData(error: true);
                }
                catch (IOException)
                {
                    return new SpgUtil.ReplyData(error: true);
                }
                return await Task.Run(() =>
                {
                    string reply;
                    try
                    {
                        reply = SerialPort.ReadLine();
                        while (SerialPort.ReadChar() != '\x06')
                        {
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        return new SpgUtil.ReplyData(error: true);
                    }
                    catch (IOException)
                    {
                        return new SpgUtil.ReplyData(error: true);
                    }
                    catch (TimeoutException)
                    {
                        return new SpgUtil.ReplyData(timedOut: true);
                    }
                    return new SpgUtil.ReplyData(reply: reply);
                });
            }
        }
        public class CommDataEventArgs : EventArgs
        {
            public CommDesc CommDesc;
            public SerialPort SerialPort; // null if comPort open
            public bool EarlyAbort;
        }
        // try to find a connected gCAU by pinging it on all com ports at different baudrates
        // and publishes an event called 'Result' which carries a CommDataEventArgs report
        // also if GUI component (normally a form) and guiCallback are both not null, call Invoke on them
        //  if member SerialPort of this report is not null, connection to found com port stays open
        // caveat: because of use of event 'DataReceived' of serial port objects,
        //        'Result' event is called from a different thread
        public class ChaseConnection
        {
            private int[] preferredBaudrates;
            private string[] portNames;
            private Dictionary<string, SerialPort> ports;
            private Dictionary<string, string> contents;
            private int currentBaudrateIndex = 0;
            private System.Timers.Timer timer;
            private int timeOut;
            private int slaveNumber;
            private bool userAbort;
            private ISynchronizeInvoke attachedComponent;
            private Action<CommDataEventArgs> attachedGuiCallback;
            // builds object and start chasing connected gcAU
            public ChaseConnection(int preferredBaudrate = 0, int timeoutInMs = 2000,
                                   ISynchronizeInvoke component = null, Action<CommDataEventArgs> guiCallback = null)
            {
                userAbort = false;
                timeOut = timeoutInMs;
                attachedComponent = component;
                attachedGuiCallback = guiCallback;
                preferredBaudrates = new int[possibleBaudrates.Length];
                int index;
                for (index = 0; index < possibleBaudrates.Length; index++)
                {
                    if (possibleBaudrates[index] == preferredBaudrate)
                    {
                        break;
                    }
                }
                int i = index < possibleBaudrates.Length ? 1 : 0;
                if (i == 1)
                {
                    preferredBaudrates[0] = preferredBaudrate;
                }
                for (int j = 0; j < possibleBaudrates.Length; j++)
                {
                    if (possibleBaudrates[j] != preferredBaudrate)
                    {
                        preferredBaudrates[i++] = possibleBaudrates[j];
                    }
                }
                portNames = SerialPort.GetPortNames();
                ports = new Dictionary<string, SerialPort>();
                contents = new Dictionary<string, string>();
                for (i = 0; i < portNames.Length; i++)
                {
                    SerialPort port = new SerialPort(portNames[i], preferredBaudrates[currentBaudrateIndex], Parity.None, 8, StopBits.Two);
                    try
                    {
                        port.WriteTimeout = 100;
                        port.Open();
                        port.DataReceived += DataReceivedHandler;
                        ports.Add(portNames[i], port);
                        contents.Add(portNames[i], string.Empty);
                        port.Write("@\r");
                    }
                    catch
                    {
                    }
                } // end for
                SetTimer();
            }
            public void abort()
            {
                userAbort = true;
            }
            // delegate for publishing result
            public event EventHandler<CommDataEventArgs> Result;
            private void SetTimer()
            {
                timer = new System.Timers.Timer(timeOut);
                timer.Elapsed += OnTimeOut;
                timer.Enabled = true;
                if (attachedComponent != null)
                {
                    timer.SynchronizingObject = attachedComponent;
                }
            }
            private void ReleaseTimer()
            {
                timer.Close();
                timer.Dispose();
            }
            private void OnResult(CommDataEventArgs data)
            {
                Result?.Invoke(this, data);
                if (attachedComponent != null && attachedGuiCallback != null)
                {
                    attachedComponent.Invoke(method: attachedGuiCallback, args: new object[] { data });
                }
            }
            private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
            {
                SerialPort sp = (SerialPort)sender;
                contents[sp.PortName] += sp.ReadExisting();
                SpgUtil.ParsedReceivedFrame frame = SpgUtil.ParseReceivedFrame(contents[sp.PortName]);
                if (frame.valid && frame.values[0] == "ECHO" && int.TryParse(frame.values[1], out slaveNumber))
                {
                    contents[sp.PortName] = string.Empty;
                    sp.Write($"@{slaveNumber}&1/RCFG/REGISTRY\r");
                    return;
                }
                if (frame.valid && frame.values[1] == "REGISTRY")
                {
                    ReleaseTimer();
                    CommDataEventArgs returnedData = new CommDataEventArgs()
                    {
                        EarlyAbort = userAbort,
                        SerialPort = sp,
                        CommDesc = new CommDesc()
                        {
                            Baudrate = preferredBaudrates[currentBaudrateIndex],
                            CommPort = sp.PortName,
                            SlaveNumber = slaveNumber,
                            ControllerSwVersion = frame.values[2],
                            RegulatorSwVersion = frame.values[3],
                            SerialNumber = frame.values[6],
                            LocalLanguage = frame.values[7],
                            LanguageSchema = -1,
                            ObjectSchema = -1,
                            ModbusTableVersion = -1,
                        }
                    };
                    returnedData.CommDesc.SiteId = SpgUtil.DecodeValue(frame.values[9]);
                    int.TryParse(frame.values[4], out returnedData.CommDesc.LanguageSchema);
                    int.TryParse(frame.values[5], out returnedData.CommDesc.ObjectSchema);
                    int.TryParse(frame.values[8], out returnedData.CommDesc.ModbusTableVersion);
                    if (returnedData.CommDesc.ObjectSchema >= 11)
                    {
                        returnedData.CommDesc.ProjectReference = SpgUtil.DecodeValue(frame.values[21]);
                    }
                    foreach (SerialPort otherSp in ports.Values)
                    {
                        if (sp != otherSp)
                        {
                            otherSp.Close();
                        }
                        else
                        {
                            sp.DataReceived -= DataReceivedHandler;
                        }
                    }
                    OnResult(returnedData);
                }
            }
            private void OnTimeOut(object sender, EventArgs e)
            {
                ReleaseTimer();
                currentBaudrateIndex++;
                if (currentBaudrateIndex >= preferredBaudrates.Length || userAbort)
                {
                    CommDataEventArgs returnedData = new CommDataEventArgs()
                    {
                        EarlyAbort = userAbort,
                        SerialPort = null,
                        CommDesc = new CommDesc()
                        {
                            Baudrate = 0,
                            CommPort = string.Empty,
                            SlaveNumber = -1,
                            ControllerSwVersion = "0",
                            RegulatorSwVersion = "0",
                            SerialNumber = "",
                            LocalLanguage = "",
                            LanguageSchema = -1,
                            ObjectSchema = -1,
                            ModbusTableVersion = -1,
                            SiteId = "",
                            ProjectReference = "",
                        }
                    };
                    foreach (SerialPort sp in ports.Values)
                    {
                        sp.Close();
                    }
                    OnResult(returnedData);
                }
                else
                {
                    try
                    {
                        foreach (SerialPort port in ports.Values)
                        {
                            port.BaudRate = preferredBaudrates[currentBaudrateIndex];
                            port.Write("@\r");
                        } // end foreach
                    }
                    catch
                    {
                    }
                    SetTimer();
                }
            }
        }

    }
    public class TeraTermLanguageUtil
    {
        private AgcObject[] poLst;
        public AgcObject[] PatchObjectList
        {
            get
            {
                return poLst;
            }
        }
        public int NbObjects
        {
            get
            {
                return poLst.Length;
            }
        }
        public static string escapeTick(string s)
        {
            return s.Replace("'", "'#39'");
        }
        public TeraTermLanguageUtil(AgcObject[] lst)
        {
            poLst = lst;
        }
        public string escapeValueInRegex(string value)
        {
            string result = string.Empty;
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                    case '^':
                    case '$':
                    case '.':
                    case '|':
                    case '?':
                    case '*':
                    case '+':
                    case '(':
                    case ')':
                    case '[':
                    case '{':
                        result += @"\" + c;
                        break;
                    default:
                        result += c;
                        break;
                }
            }
            return (result);
        }
        public string SendStr(int index)
        {
            if (index < 0 || index > NbObjects)
                throw new ArgumentException(string.Format("Index out-of-range: {0}", index));
            string wcfg = string.Format("@&0/WCFG/{0}", poLst[index].objectName);
            int i;
            for (i = 0; i < poLst[index].attributes.Length; i++)
            {
                wcfg += string.Format("/{0}:{1}",
                  poLst[index].attributes[i].position, SpgUtil.EncodeValue(poLst[index].attributes[i].value));
            }
            string checkSum = SpgUtil.CalculateChecksum(wcfg + "\x16").asHexString;
            return string.Format("'{0}'#$16'{1}'", escapeTick(wcfg), checkSum);
        }
        public string RegexOK(int index)
        {
            if (index < 0 || index > NbObjects)
                throw new ArgumentException(string.Format("Index out-of-range: {0}", index));
            string ignoredParamRegexFormat = @"(/(\/|[^/])*){0}{1}{2}";
            string regex = string.Format("/WCFG/{0}", poLst[index].objectName);
            int attrIndex, posAttr, lastPosAttr;
            bool hit;
            for (attrIndex = 0, posAttr = 1, lastPosAttr = 0; posAttr <= poLst[index].totalNumberOfAttributes + 1; posAttr++)
            {
                hit = attrIndex < poLst[index].attributes.Length &&
                      poLst[index].attributes[attrIndex].position == posAttr;
                if (hit && poLst[index].attributes[attrIndex].readOnly)
                {
                    hit = false;
                    attrIndex++;
                }
                if (posAttr > poLst[index].totalNumberOfAttributes || hit)
                {
                    int nbIgnoredParms = posAttr - 1 - lastPosAttr;
                    if (nbIgnoredParms > 0)
                        regex += string.Format(ignoredParamRegexFormat, "{", nbIgnoredParms, "}");
                }
                if (hit)
                {
                    regex += string.Format("/{0}",
                      escapeValueInRegex(SpgUtil.EncodeValue(poLst[index].attributes[attrIndex].value)));
                    attrIndex++;
                    lastPosAttr = posAttr;
                }
            }
            return string.Format("'{0}'", escapeTick(regex));
        }
    }
}