using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Scripts.Scriptor.Historator
{
    public class Archive
    {
        private static Archive s_mInstance = null;
        private String mOutputPath = null;
        public List<ScriptRecord> ScriptRecords { get; set; }

        public static Archive GetArchive() => s_mInstance;
        public void Change(Type typeFullName, string methodName, string parameter, object value)
        {
            string typeID = string.Format("{0}.{1}", typeFullName.FullName, methodName);
            if (ScriptRecords == null)
            {
                ScriptRecords = new List<ScriptRecord>();    
            }
            
            ScriptRecord record = ScriptRecords?.Find(record => record.TypeID == typeID);

            if (record == null)
            {
                record = new ScriptRecord { TypeID = typeID};
                ScriptRecords.Add(record);
            }

            Parameter recordParameter = record.Values?.Find(parameterIter => parameterIter.Name == parameter);

            if (recordParameter == null)
            {
                recordParameter = new Parameter { Name = parameter };
                if(record.Values != null)
                {
                    record.Values.Add(recordParameter);
                }
                else
                {
                    record.Values = new List<Parameter> { recordParameter };
                }   
            }

            //Probably need a switch for enum and object handling of the support is ever added.
            //We'll deal with that road when we come to it. For now we'll just parse in strings
            //based on reflection of object type pulled from the typeID and parameter name.
            recordParameter.Value = value?.ToString();
        }

        public string Retrieve(Type typeFullName, string typeID, string parameter)
        {
            return ScriptRecords?.Find(record => record.TypeID == string.Format("{0}.{1}", typeFullName.FullName, typeID))?.Values?.Find(paraFind => paraFind.Name == parameter)?.Value as String;
        }
        public void Commit()
        {
            Save(mOutputPath);
        }

        public static Archive Load(string path)
        {
            Archive retVal = null;
            FileInfo fInfo = new FileInfo(path);
            if (fInfo.Exists)
            {
                XmlSerializer xml = new XmlSerializer(typeof(Archive));

                using (FileStream stream = fInfo.OpenRead())
                {
                    retVal = xml.Deserialize(stream) as Archive;
                }

                retVal.mOutputPath = path;
            }
            return s_mInstance = ( retVal == null ? new Archive { mOutputPath = path } : retVal);
        }

        private void Save(string path)
        {
            XmlSerializer xml = new XmlSerializer(typeof(Archive));
            FileInfo fInfo = new FileInfo(path);
            if (fInfo.Exists)
            {
                fInfo.Delete();
            }

            using(FileStream stream = File.OpenWrite(path))
            {
                xml.Serialize(stream, this);
            }
        }
    }
}
