using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Babylon.ResourcesProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NgxTranslateResourceProvider
{
    public class NgxTranslateResourceProvider : IResourcesProvider
    {
        string _storageLocation;
        /// <summary>
        /// The StorageLocation will be set by the user when creating a new generic localization project in Babylon.NET. It can be a path to a folder, a file name,
        /// a database connection string or any other information needed to access the resource files.
        /// </summary>
        public string StorageLocation
        {
            get
            {
                return _storageLocation;
            }

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(value);

                _storageLocation = value;
            }
        }

        /// <summary>
        /// This text is displayed to the user as label to the storage location textbox/combobox when setting up the resource provider.
        /// </summary>
        public string StorageLocationUserText
        {
            get
            {
                return "Base Directory where language files are located";
            }
        }

        /// <summary>
        /// This is the type of storage used be the provider. Depending on the type Babylon.NET will display a FileSelectionControl, a DirectorySelectionControl 
        /// or a simple TextBox as StorageLocation input control.
        /// </summary>
        public StorageType StorageType
        {
            get
            {
                return StorageType.Directory;
            }
        }

        /// <summary>
        /// This is the description of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Description
        {
            get
            {
                return "JSON Resources Provider for ngx-tranlate JSON files.";
            }
        }

        /// <summary>
        /// This is the name of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Name
        {
            get
            {
                return "Ngx-Translate JSON Resources Provider";
            }
        }

        /// <summary>
        /// Babylon.NET will pass the path to the current solution to the provider. This can for example be used to work with relative paths.
        /// </summary>
        public string SolutionPath { get; set; }

        public void ExportResourceStrings(string projectName, string projectLocale, IReadOnlyCollection<string> localesToExport, ICollection<StringResource> resourceStrings, ResourceStorageOperationResultDelegate resultDelegate)
        {
            // We use a dictionary as cache for the resources for each file
            Dictionary<string, JObject> fileCache = new Dictionary<string, JObject>();

            // We keep an error list with files that cannot be written to avoid the same error over and over
            List<string> errorList = new List<string>();

            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();

            foreach (string locale in localesToExport)
            {
                // assemble file name
                string filename = Path.Combine(baseDirectory, string.Format("{0}.json", string.IsNullOrWhiteSpace(locale) ? projectLocale : locale)).Replace("..", ".");

                Dictionary<string, string> dictionary = new Dictionary<string, string>();

                // loop over all strings...
                foreach (var resString in resourceStrings)
                {
                    dictionary.Add(resString.Name, resString.GetLocaleText(string.IsNullOrWhiteSpace(locale) ? "nn-nn" : locale));
                }

                JObject jObject = new JObject();


                foreach (var item in dictionary)
                {
                    string[] keys = item.Key.Split('.');
                    if (keys.Count() == 1)
                        jObject[item.Key] = item.Value;
                    else
                    {
                        JObject newObject = new JObject();
                        var token = jObject.SelectToken(string.Join(".", keys.Take(keys.Length - 1)));
                        if (token != null)
                            token[keys[keys.Length - 1]] = item.Value;
                        else
                        {
                            bool added = false;
                            for (int i = keys.Length - 1; i > 0; i--)
                            {
                                newObject[keys[keys.Length - 1]] = item.Value;
                                string currentKey = string.Join(".", keys.Take(i));
                                if (ContainsKey(jObject, currentKey))
                                {
                                    var newKey = keys.Skip(i).ToArray();
                                    newKey = newKey.Take(newKey.Length - 1).ToArray();
                                    JObject c = GetByKey(jObject, currentKey);
                                    if (newKey.Length == 1)
                                    {
                                        c.Value<JObject>().Add(new JProperty(string.Join(".", newKey), newObject));
                                    }
                                    else
                                    {
                                        newObject[keys[keys.Length - 1]] = item.Value;
                                        for (int a = newKey.Length - 1; a > 0; a--)
                                        {
                                            var childObject = newObject;
                                            newObject = new JObject();
                                            newObject[newKey[a]] = childObject;
                                        }

                                        if (!c.ContainsKey(newKey[0])) { 
                                            c.Value<JObject>().Add(new JProperty(newKey[0], newObject));
                                        }
                                    }
                                    added = true;
                                }
                            }

                            if (!added)
                            {
                                for (int i = keys.Length - 1; i > 0; i--)
                                {
                                    if (i == keys.Length - 1)
                                    {
                                        newObject[keys[keys.Length - 1]] = item.Value;
                                    }
                                    else
                                    {
                                        var childObject = newObject;
                                        newObject = new JObject();
                                        newObject[keys[i]] = childObject;
                                    }
                                }
                                jObject[keys[0]] = newObject;
                            }
                        }
                    }
                }

                fileCache.Add(filename, jObject);
            }

            // save all dictionaries in cache
            foreach (var item in fileCache)
            {
                ResourceStorageOperationResultItem resultItem = new ResourceStorageOperationResultItem(item.Key);
                resultItem.ProjectName = projectName;

                try
                {
                    // serialize the JSON file
                    using (StreamWriter fileStream = File.CreateText(item.Key))
                    {
                        fileStream.Write(JsonConvert.SerializeObject(item.Value, Formatting.Indented));

                        // report success
                        resultDelegate?.Invoke(resultItem);
                    }
                }
                catch (Exception ex)
                {
                    // report error
                    if (resultDelegate != null)
                    {
                        resultItem.Result = ResourceStorageOperationResult.Error;
                        resultItem.Message = ex.GetBaseException().Message;
                        resultDelegate(resultItem);
                    }
                }
            }
        }

        public ICollection<StringResource> ImportResourceStrings(string projectName, string projectLocale)
        {
            // We use a Dictionary to keep a list of all StringResource object searchable by the key.
            Dictionary<string, StringResource> workingDictionary = new Dictionary<string, StringResource>();

            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();

            // iterate over the whole folder tree starting from the base directory.
            foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.json", SearchOption.AllDirectories))
            {
                // get locale from file name
                string locale = Path.GetFileNameWithoutExtension(file).TrimStart(new char[] { '.' });
                locale = locale.Replace(projectLocale, "");

                using (StreamReader fileStream = File.OpenText(file))
                {
                    JObject jsonObject = JObject.Parse(fileStream.ReadToEnd());
                    IEnumerable<JToken> jTokens = jsonObject.Descendants().Where(p => p.Count() == 0);
                    Dictionary<string, string> results = jTokens.Aggregate(new Dictionary<string, string>(), (properties, jToken) =>
                    {
                        properties.Add(jToken.Path, jToken.ToString());
                        return properties;
                    });

                    foreach (var item in results)
                    {
                        StringResource stringRes;
                        string relativeDirectory = Path.GetDirectoryName(file).Substring(baseDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                        string plainFilename = projectName; // we must use project name as filename as filename would be a locale

                        // check whether we already have the string
                        if (!workingDictionary.TryGetValue(item.Key, out stringRes))
                        {
                            string name = plainFilename;
                            if (item.Key.Contains("."))
                                name = string.Format("{0}.{1}", name, item.Key.Substring(0, item.Key.IndexOf("."))).Replace(".", @"\");

                            stringRes = new StringResource(item.Key, "");
                            stringRes.StorageLocation = name;
                            workingDictionary.Add(item.Key, stringRes);
                        }

                        // add locale text. Babylon.NET uses an empty string as locale for the invariant language. A StringResource is only valid if the invariant language is set. 
                        // StringResources without an invariant language text are discared by Babylon.NET.
                        stringRes.SetLocaleText(locale, item.Value);
                    }
                }
            }

            // get collection of stringResources
            List<StringResource> result = new List<StringResource>();
            workingDictionary.ToList().ForEach(i => result.Add(i.Value));
            return result;
        }

        private string GetBaseDirectory()
        {
            string baseDirectory = _storageLocation;
            if (!Path.IsPathRooted(baseDirectory))
            {
                baseDirectory = Path.GetFullPath(Path.Combine(SolutionPath, baseDirectory));
            }

            return baseDirectory;
        }

        private bool ContainsKey(JObject jObject, string key)
        {
            string[] keys = key.Split('.');
            JObject workingObject = jObject;
            foreach (string k in keys)
            {
                if (workingObject.ContainsKey(k))
                    workingObject = workingObject[k] as JObject;
                else
                    return false;
            }

            return true;
        }

        private JObject GetByKey(JObject jObject, string key)
        {
            string[] keys = key.Split('.');
            JObject workingObject = jObject;
            foreach (string k in keys)
            {
                workingObject = workingObject[k] as JObject;
            }

            return workingObject;
        }
    }
}
