using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using QiHe.Yaml.Grammar;

//
// Deals with a list of YAML documents describing gCAU configuration classes versus their versions
//

namespace YamlForClasses
{
    public struct GcauClassVersionDef
    {
        public GcauClassVersionDef(int minClasses, int instances, int cfgAttributes)
        {
            MinClasses = minClasses;       // minimum version for a class definition
            Instances = instances;         // number of instances for this version (0 means class name = instance name)
            CfgAttributes = cfgAttributes; // number of attributes for this class version
        }

        public int MinClasses { get; }
        public int Instances { get; }
        public int CfgAttributes { get; }
    }

    // different definitions for a class (not bearing its name) as a List<GcauClassVersionDef:struct>
    // also: defines selectedIndex which points to the current version a connected gCAU needs to abide by
    public class ClassDefs
    {
        public List<GcauClassVersionDef> ClassDefinitions;
        private int selectedIndex; // direct index when actual classes version is dynamically known

        public ClassDefs()
        {
            ClassDefinitions = new List<GcauClassVersionDef>();
            selectedIndex = -1; // invalid, meaning we cannot tell which class definition applies
        }

        // given a known version of classes tries to figure out which index to list of definitions applies
        // if found selectedIndex member is updated and true is returned
        // otherwise selectedIndex is set to -1 and false is returned
        public bool SetSelectedIndex(int actualClassVersion)
        {
            int maxClasses = -1;
            selectedIndex = -1;
            // here it is not assumed list is sorted by MinClasses version
            // so search covers the entire list
            for (int i = 0; i < ClassDefinitions.Count; i++)
            {
                int minClasses = ClassDefinitions[i].MinClasses;
                if (maxClasses < minClasses && actualClassVersion >= minClasses)
                {
                    selectedIndex = i;
                    maxClasses = minClasses;
                }
            }
            return selectedIndex >= 0;
        }

        public int SelectedIndex()
        {
            if (selectedIndex < 0 || selectedIndex >= ClassDefinitions.Count)
            {
                return -1;
            }
            return selectedIndex;
        }
    }

    public class GcauClassRestrictions
    {
        public bool Valid; // YAML document has been successfully parsed
        public string Error;
        public int MaxVersion; // what YAML document states as latest version
        private int _targetClassVersion; // value of the connected gCAU (-1 if unknown or Valid is false)
        public int TargetClassVersion
        {
            get { return _targetClassVersion; }
            set { _targetClassVersion = Valid ? value : -1; }
        }
        public Dictionary<string, ClassDefs> ClassHash;

        public GcauClassRestrictions()
        {
            this.Valid = false;
            this.Error = "Void of any definitions";
            this.MaxVersion = 0;
            this.ClassHash = new Dictionary<string, ClassDefs>();
            this._targetClassVersion = -1;
        }

        public int GetNumberOfCfgAttributes(string objectName, int classesVersion)
        {
            return -1;
        }
    }

    public static class YamlClassesProcessor
    {
        private static Regex classNameSplitRx;

        public struct ClassInstanceBreakdown
        {
            public string className; // ANIX_1 will be stored as ANIX
            public int instance; // ANIX_1 => 1, SYSTEM => 0
        }

        static YamlClassesProcessor()
        {
            classNameSplitRx = new Regex(@"^(.+)_(\d+)$", RegexOptions.Compiled);
        }

        public static ClassInstanceBreakdown SplitObjectName(string obj)
        {
            ClassInstanceBreakdown result = new ClassInstanceBreakdown();
            Match match = classNameSplitRx.Match(obj);
            if (match.Success == true)
            {
                result.className = match.Groups[1].Value;
                result.instance = int.Parse(match.Groups[2].Value);
            }
            else
            {
                result.className = obj;
                result.instance = 0;
            }
            return result;
        }

        public static GcauClassRestrictions YamlClassesParser(List<YamlDocument> documents, string filename)
        {
            GcauClassRestrictions result = new GcauClassRestrictions();
            if (documents.Count != 2)
            {
                result.Error = String.Format("Error in {0}: does not contain two documents", filename);
                return result;
            }
            foreach (YamlDocument doc in documents)
            {
                if (!(doc.Root is Mapping))
                {
                    result.Error = String.Format("Error in {0}: main entries of documents are not of Mapping type", filename);
                    return result;
                }
            }
            List<MappingEntry> entries0 = ((Mapping)documents[0].Root).Enties;
            if (entries0.Count != 1)
            {
                result.Error = String.Format("Error in {0}: first document has not only one entry", filename);
                return result;
            }
            if (entries0[0].Key.ToString() != "maxClasses")
            {
                result.Error = String.Format("Error in {0}: first document has no entry 'maxClasses'", filename);
                return result;
            }
            if (!Int32.TryParse(entries0[0].Value.ToString(), out result.MaxVersion))
            {
                result.Error = String.Format("Error in {0}: 'maxClasses' in first document is not an integer", filename);
                return result;
            }
            foreach (MappingEntry classDef in ((Mapping)documents[1].Root).Enties)
            {
                string className = classDef.Key.ToString();
                result.ClassHash.Add(className, new ClassDefs());
                if (!(classDef.Value is Sequence))
                {
                    result.Error = String.Format("Error in {0} with definition of {1}: not a list", filename, className);
                    return result;
                }
                List<DataItem> listClassDefs = ((Sequence)classDef.Value).Enties;
                for (int i = 0; i < listClassDefs.Count; i++)
                {
                    DataItem classDefMap = listClassDefs[i];
                    if (!(classDefMap is Mapping))
                    {
                        result.Error = String.Format("Error in {0} with definition of {1}: list element {2} is not a simple mapping", filename, className, i + 1);
                        return result;
                    }
                    Dictionary<string, int> classDefs = new Dictionary<string, int>() { { "minClasses", -1 }, { "instances", -1 }, { "cfgAttributes", -1 } };
                    foreach (MappingEntry classDefEntry in ((Mapping)classDefMap).Enties)
                    {
                        string key = classDefEntry.Key.ToString();
                        if (!classDefs.ContainsKey(key))
                        {
                            result.Error = String.Format("Error in {0} with definition of {1}, index {2}: key '{3}' is unexpected", filename, className, i + 1, key);
                            return result;
                        }
                        if (!(classDefEntry.Value is Scalar))
                        {
                            result.Error = String.Format("Error in {0} with definition of {1}, index {2}, key {3}: not a plain scalar", filename, className, i + 1, key);
                            return result;
                        }
                        if (classDefs[key] != -1)
                        {
                            result.Error = String.Format("Error in {0} with definition of {1}, index {2}, key {3}: defined multiple times", filename, className, i + 1, key);
                            return result;
                        }
                        int value;
                        if (!Int32.TryParse(classDefEntry.Value.ToString(), out value) || value < 0)
                        {
                            result.Error = String.Format("Error in {0} with definition of {1}, index {2}, key {3}: value is not a valid positive integer", filename, className, i + 1, key);
                            return result;
                        }
                        classDefs[key] = value;
                    }
                    foreach (KeyValuePair<string, int> kv in classDefs)
                    {
                        if (kv.Value < 0)
                        {
                            result.Error = String.Format("Error in {0} with definition of {1}, index {2}: key '{3}' not defined", filename, className, i + 1, kv.Key);
                            return result;
                        }
                    }
                    result.ClassHash[className].ClassDefinitions.Add(new GcauClassVersionDef(classDefs["minClasses"], classDefs["instances"], classDefs["cfgAttributes"]));
                }
            }
            result.Error = "No error";
            result.Valid = true;
            return result;
        }

    }
}