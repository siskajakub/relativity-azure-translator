# relativity-azure-translator
Relativity mass event handled for text translation using Azure Translator.

# Install
## 1) Create Instance Settings
Create required Relativity Instance Settings entries:  
Name | Section | Value Type | Value (example) | Description
---- | ------- | ---------- | --------------- | -----------
AzureServiceRegion | Azure.Translator | Text | xxxxxxxxx | Azure Translator Service Region.
AzureSubscriptionKey | Azure.Translator | Text | xxxxxxxxx | Azure Translator Subscription Key.
AzureTranslatorEndpoint | Azure.Translator | Text | xxxxxxxxx | Azure Translator Endpoint.
Cost1MCharacters | Azure.Translator | Text | 10 | Azure Translator cost per 1 million characters. Can be decimal number.
DestinationField | Azure.Translator | Text | Extracted Text Translated | Document Field where to record the translated text.
LogField | Azure.Translator | Text | Extracted Text | Document Field to store the translation log.
SourceField | Azure.Translator | Text | Extracted Text | Document Field with the text to translate.
TranslateFrom | Azure.Translator | Text | auto | Language to translate from ("auto" for automatic detection).
TranslateTo | Azure.Translator | Text | en | Language to translate to.

## 2) Compile DLL
Download the source code and compile the code using Microsoft Visual Studio 2019.  
For more details on how to setup your development environemnt, please follow official [Relativity documentation](https://platform.relativity.com/10.3/index.htm#Relativity_Platform/Setting_up_your_development_environment.htm).  
You can also use precompiled DLL from the repository.

## 3) Upload DLL
Upload `RelativityAzureTranslator.dll` to Relativity Resource Files.

## 4) Add to Workspace
For desired workspaces add mass event handler to Document Object:
* Browse to Document Object (Workspace->Workspace Admin->Object Type->Document)
* In Mass Operations section click New and add the handler:
  * Name: Azure.Translator
  * Pop-up Directs To: Mass Operation Handler
  * Select Mass Operation Handler: RelativityAzureTranslator.dll

# Translation Language
Translation language can be set only on instance level with Relativity Instance Settings:
* TranslateFrom
* TranslateTo

For details on language options, please refer to official [Azure documentation](https://docs.microsoft.com/en-us/azure/cognitive-services/translator/language-support).

# Log
Event handler generates translation log to fiels specified by the Relativity Instance Settings.  
Log entry is added after each translation. There can be multiple log entries for one Document.  
Log entry has following fields:
* Translation engine
* User email address
* Timestamp
* Language translated from ("auto" for automatic detection)
* Language translated to
* Character count of the source text
* Character count of the translated text

Translation log can be viewed from the Relativity front-end via attached Relativity Script.

# Notes
Relativity Azure Translator mass event handled was developed and tested in Relativity 10.3.  
Relativity Azure Translator mass event handled works correctly only with UTF-8 text.
