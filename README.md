# ngx-translate Resources Provider for Babylon

The provider will read all ngx-translate Json files in the specified directory and treat them as files containing string resources. Files are named using the pattern <culture code>.json. The provider assumes invariant strings are contained in the file matching the culture code of the Babylon solution. 
  
All files containing culture codes (e.g. de.json) will be treated as translations. Strings not present in the invariant file are ignored. 

Relative paths are fully supported. 

Subfolders of the base directory are also processed. 

Comments are not supported.
    
