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
DestinationField | Azure.Translator | Text | Extracted Text Translated | Document Field where to record translated text.
SourceField | Azure.Translator | Text | Extracted Text | Document Field with text to translate.

## 2) Compile DLL
Download the source code and compile the code using Microsoft Visual Studio 2019.  
For more details on how to setup your development environemnt, please follow official [Relativity documentation](https://platform.relativity.com/10.3/index.htm#Relativity_Platform/Setting_up_your_development_environment.htm).

## 3) Upload DLL
Upload `RelativityAzureTranslator.dll` to Relativity Resource Files.

## 4) Add to Workspace
For desired workspaces add mass event handler to Document Object:
* Browse to Document Object (Workspace->Workspace Admin->Object Type->Document)
* In Mass Operations section click New and add the handler:
  * Name: Azure.Translator
  * Pop-up Directs To: Mass Operation Handler
  * Select Mass Operation Handler: RelativityAzureTranslator.dll

# Notes
Relativity Azure Translator mass event handled was developed and tested in Relativity 10.3.  
Relativity Azure Translator mass event handled works correctly only with UTF-8 text.
