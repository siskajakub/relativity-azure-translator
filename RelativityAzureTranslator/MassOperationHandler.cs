using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Relativity.API;
using Relativity.Kepler.Transport;
using Relativity.Services.Objects;
using Relativity.Services.Objects.DataContracts;

namespace RelativityAzureTranslator
{
    [Description("Relativity Azure Translator")]
    [Guid("7db432d7-09c1-44c9-8330-8a1f3ef28849")]

    /*
     * Relativity Mass EventHandler Class
     */
    public class MassOperationHandler : kCura.MassOperationHandlers.MassOperationHandler
    {
        /*
         * Occurs after the user has selected items and pressed go.
         * In this function you can validate the items selected and return a warning/error message.
         */
        public override kCura.EventHandler.Response ValidateSelection()
        {
            // Get logger
            Relativity.API.IAPILog _logger = this.Helper.GetLoggerFactory().GetLogger().ForContext<MassOperationHandler>();

            // Init general response
            kCura.EventHandler.Response response = new kCura.EventHandler.Response()
            {
                Success = true,
                Message = ""
            };

            // Get current Workspace ID
            int workspaceId = this.Helper.GetActiveCaseID();
            _logger.LogDebug("Azure Translator, current Workspace ID: {workspaceId}", workspaceId.ToString());

            // Check if all Instance Settings are in place
            IDictionary<string, string> instanceSettings = this.GetInstanceSettings(ref response, new string[] { "SourceField", "DestinationField", "Cost1MCharacters", "AzureServiceRegion", "AzureSubscriptionKey", "AzureTranslatorEndpoint" });
            // Check if there was not error
            if (!response.Success)
            {
                return response;
            }

            /*
             * Calculate the translation costs
             */
            // TODO: redo in Object Manager API
            try
            {
                // Construct and execute SQL Query to get the characters count
                string sqlText = "SELECT SUM(LEN(CAST([" + instanceSettings["SourceField"].Replace(" ","") + "] AS NVARCHAR(MAX)))) FROM [EDDSDBO].[Document] AS [Document] JOIN [Resource].[" + this.MassActionTableName + "] AS [MassActionTableName] ON [Document].[ArtifactID] = [MassActionTableName].[ArtifactID]";
                _logger.LogDebug("Azure Translator, translation cost SQL Parameter and Query: {query}", sqlText);
                long count = (long)this.Helper.GetDBContext(workspaceId).ExecuteSqlStatementAsScalar(sqlText);

                // Calculate translation cost
                float cost = (count / 1000000f) * float.Parse(instanceSettings["Cost1MCharacters"]);
                _logger.LogDebug("Azure Translator, translation cost: {price}CHF, ({chars} chars)", cost.ToString("0.00"), count.ToString());
                response.Message = string.Format("Translation cost is {0}CHF, ({1} chars)", cost.ToString("0.00"), count.ToString());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Azure Translator, translation cost error ({SourceField}, {Cost1MCharacters})", instanceSettings["SourceField"], instanceSettings["Cost1MCharacters"]);

                response.Success = false;
                response.Message = "Translation cost error";
            }

            return response;
        }

        /*
         * Occurs after the user has inputted data to a layout and pressed OK.
         * This function runs as a pre-save eventhandler.
         * This is NOT called if the mass operation does not have a layout.
         */
        public override kCura.EventHandler.Response ValidateLayout()
        {
            // Init general response
            kCura.EventHandler.Response response = new kCura.EventHandler.Response()
            {
                Success = true,
                Message = ""
            };
            return response;
        }

        /*
         * Occurs before batching begins. A sample use would be to set up an instance of an object.
         */
        public override kCura.EventHandler.Response PreMassOperation()
        {
            // Init general response
            kCura.EventHandler.Response response = new kCura.EventHandler.Response()
            {
                Success = true,
                Message = ""
            };
            return response;
        }

        /*
         * This function is called in batches based on the size defined in configuration.
         */
        public override kCura.EventHandler.Response DoBatch()
        {
            // Get logger
            Relativity.API.IAPILog _logger = this.Helper.GetLoggerFactory().GetLogger().ForContext<MassOperationHandler>();

            // Init general response
            kCura.EventHandler.Response response = new kCura.EventHandler.Response()
            {
                Success = true,
                Message = ""
            };

            // Get current Workspace ID
            int workspaceId = this.Helper.GetActiveCaseID();
            _logger.LogDebug("Azure Translator, current Workspace ID: {workspaceId}", workspaceId.ToString());

            // Check if all Instance Settings are in place
            IDictionary<string, string> instanceSettings = this.GetInstanceSettings(ref response, new string[] { "SourceField", "DestinationField", "Cost1MCharacters", "AzureServiceRegion", "AzureSubscriptionKey", "AzureTranslatorEndpoint" });
            // Check if there was not error
            if (!response.Success)
            {
                return response;
            }

            // Update general status
            this.ChangeStatus("Translating documents");

            // For each document create translation task
            List<Task<int>> translationTasks = new List<Task<int>>();
            int runningTasks = 0;
            int concurrentTasks = 16;
            for (int i = 0; i < this.BatchIDs.Count; i++)
            {
                // Translate documents in Azure and update Relativity using Object Manager API
                translationTasks.Add(TranslateDocument(workspaceId, this.BatchIDs[i], instanceSettings["SourceField"], instanceSettings["DestinationField"], instanceSettings["AzureServiceRegion"], instanceSettings["AzureSubscriptionKey"], instanceSettings["AzureTranslatorEndpoint"]));

                // Update progreass bar
                this.IncrementCount(1);

                // Allow only certain number of tasks to run concurrently
                do
                {
                    runningTasks = 0;
                    foreach (Task<int> translationTask in translationTasks)
                    {
                        if (!translationTask.IsCompleted)
                        {
                            runningTasks++;
                        }
                    }
                    if (runningTasks >= concurrentTasks)
                    {
                        Thread.Sleep(100);
                    }
                } while (runningTasks >= concurrentTasks);
            }

            // Update general status
            this.ChangeStatus("Waiting to finish the document translation");

            // Wait for all translations to finish
            _logger.LogDebug("Azure Translator, waiting for all documents finish translating ({n} document(s))", this.BatchIDs.Count.ToString());
            Task.WaitAll(translationTasks.ToArray());

            // Update general status
            this.ChangeStatus("Checking the results of the document translation");

            // Check results
            List<string> translationErrors = new List<string>();
            for (int i = 0; i < translationTasks.Count; i++)
            {
                // If translation was not done add to the error List
                _logger.LogDebug("Azure Translator, translation task result: {result} (task: {task})", translationTasks[i].Result.ToString(), translationTasks[i].Id.ToString());
                if (translationTasks[i].Result != 0)
                {
                    translationErrors.Add(translationTasks[i].Result.ToString());
                }
            }

            // If there are any errors adjust response
            if (translationErrors.Count > 0)
            {
                _logger.LogError("Azure Translator, not all documents have been translated: ({documents})", string.Join(", ", translationErrors));

                response.Success = false;
                response.Message = "Not all documents have been translated";
            }

            return response;
        }

        /*
         * Occurs after all batching is completed.
         */
        public override kCura.EventHandler.Response PostMassOperation()
        {
            // Init general response
            kCura.EventHandler.Response response = new kCura.EventHandler.Response()
            {
                Success = true,
                Message = ""
            };
            return response;
        }

        /*
         * Custom method to get required Relativity Instance Settings
         */
        private IDictionary<string, string> GetInstanceSettings(ref kCura.EventHandler.Response response, string[] instanceSettingsNames)
        {
            // Get logger
            Relativity.API.IAPILog _logger = this.Helper.GetLoggerFactory().GetLogger().ForContext<MassOperationHandler>();

            // Output Dictionary
            IDictionary<string, string> instanceSettingsValues = new Dictionary<string, string>();

            // Get and validate instance settings
            foreach (string name in instanceSettingsNames)
            {
                try
                {
                    instanceSettingsValues.Add(name, this.Helper.GetInstanceSettingBundle().GetString("Azure.Translator", name));
                    if (instanceSettingsValues[name].Length <= 0)
                    {
                        _logger.LogError("Azure Translator, Instance Settings empty error: {section}/{name}", "Azure.Translator", name);

                        response.Success = false;
                        response.Message = "Instance Settings error";
                        return instanceSettingsValues;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Azure Translator, Instance Settings error: {section}/{name}", "Azure.Translator", name);

                    response.Success = false;
                    response.Message = "Instance Settings error";
                    return instanceSettingsValues;
                }

                _logger.LogDebug("Azure Translator, Instance Setting: {name}=>{value}", name, instanceSettingsValues[name]);
            }

            // Check Cost1MCharacters Instance Settings is a number
            try
            {
                float.Parse(instanceSettingsValues["Cost1MCharacters"]);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Azure Translator, Instance Settings error: {section}/{name}", "Azure.Translator", "Cost1MCharacters");

                response.Success = false;
                response.Message = "Instance Settings error";
                return instanceSettingsValues;
            }

            return instanceSettingsValues;
        }

        /*
         * Custom method to translate document using Azure Translator
         */
        private async Task<int> TranslateDocument(int workspaceId, int documentArtifactId, string sourceField, string destinationField, string azureServiceRegion, string azureSubscriptionKey, string azureTranslatorEndpoint)
        {
            /*
             * Custom local function to split string into chunks of defined size with delimiter priority
             */
            List<string> SplitMulti(string str, char[] delimiters, int minChunkThreshold, int maxChunkThreshold, int smallChunkThreshold)
            {
                List<string> chunks = new List<string>() { str };
                List<string> subChunks;
                foreach (char delimiter in delimiters)
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        // Check if chunks are all below threshold otherwise split by space
                        if (chunks[i].Length > maxChunkThreshold)
                        {
                            subChunks = SplitSized(chunks[i], delimiter, minChunkThreshold, maxChunkThreshold, smallChunkThreshold);
                            chunks.RemoveAt(i);
                            chunks.InsertRange(i, subChunks);
                        }
                    }
                }

                return chunks;
            }

            /*
             * Custom local function to split string into chunks of defined size
             */
            List<string> SplitSized(string str, char delimiter, int minChunkThreshold, int maxChunkThreshold, int smallChunkThreshold)
            {
                string[] split = str.Split(delimiter);
                List<string> chunks = new List<string>();

                string hlp = split[0];
                for (int i = 1; i < split.Length; i++)
                {
                    // Rearange split text into bigger chunks
                    if (((hlp.Length + split[i].Length) < minChunkThreshold || split[i].Length < smallChunkThreshold) && ((hlp.Length + split[i].Length) < maxChunkThreshold))
                    {
                        hlp = hlp + delimiter + split[i];
                    }
                    else
                    {
                        chunks.Add(hlp + delimiter);
                        hlp = split[i];
                    }
                }
                chunks.Add(hlp);

                return chunks;
            }
            
            // Get logger
            Relativity.API.IAPILog _logger = this.Helper.GetLoggerFactory().GetLogger().ForContext<MassOperationHandler>();

            // Get Relativity Object Manager API
            IObjectManager objectManager = this.Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.CurrentUser);

            // Get document text
            Stream streamToTranslate;
            try
            {
                // Construct objects for document retriaval
                RelativityObjectRef relativityObject = new RelativityObjectRef
                {
                    ArtifactID = documentArtifactId
                };
                FieldRef relativityField = new FieldRef
                {
                    Name = sourceField
                };
                IKeplerStream keplerStream = await objectManager.StreamLongTextAsync(workspaceId, relativityObject, relativityField);
                streamToTranslate = await keplerStream.GetStreamAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Azure Translator, document for translation retrieval error (ArtifactID: {id})", documentArtifactId.ToString());
                return documentArtifactId;
            }

            // Copy stream to text as that is used for further on
            string textToTranslate = new StreamReader(streamToTranslate).ReadToEnd();
            streamToTranslate.Dispose();

            // Log original document
            _logger.LogDebug("Azure Translator, original document (ArtifactID: {id}, length: {length})", documentArtifactId.ToString(), textToTranslate.Length.ToString());

            // Split document text to chunks as Azure Translator request is limited by 10K characters
            List<string> partsToTranslate = SplitMulti(textToTranslate, new char[] { '.', ' '}, 9000, 9900, 20);
            _logger.LogDebug("Azure Translator, document split into {n} parts (ArtifactID: {id})", partsToTranslate.Count.ToString(), documentArtifactId.ToString());

            // Do Azure Translator call for every text part
            List<string> partsTranslated = new List<string>();
            for (int i = 0; i < partsToTranslate.Count; i++)
            {
                // Build translation request
                HttpRequestMessage request = new HttpRequestMessage();
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(azureTranslatorEndpoint + "translate?api-version=3.0&to=en&includeAlignment=true"); // https://docs.microsoft.com/azure/cognitive-services/translator/reference/v3-0-translate
                request.Content = new StringContent(JsonConvert.SerializeObject(new object[] { new { Text = partsToTranslate[i] } }), Encoding.UTF8, "application/json");
                request.Headers.Add("Ocp-Apim-Subscription-Key", azureSubscriptionKey);
                request.Headers.Add("Ocp-Apim-Subscription-Region", azureServiceRegion);

                // Send the request
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

                // Check the response
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.LogError("Azure Translator, HTTP reposnse error (ArtifactID: {id}, status: {status})", documentArtifactId.ToString(), response.StatusCode.ToString());
                    return documentArtifactId;
                }

                // Read the response
                string partTranslated = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Azure Translator, translation result JSON check (ArtifactID: {id}, length: {length})", documentArtifactId.ToString(), partTranslated.Length.ToString());

                // Parse JSON
                TranslationResult[] translationResults = JsonConvert.DeserializeObject<TranslationResult[]>(partTranslated);

                // Check the translation result
                if (translationResults.Length > 1 || translationResults[0].Translations.Length > 1)
                {
                    _logger.LogError("Azure Translator, unexpected document translation results (ArtifactID: {id})", documentArtifactId.ToString());
                    return documentArtifactId;
                }
                if (translationResults.Length == 0 || translationResults[0].Translations.Length == 0)
                {
                    _logger.LogError("Azure Translator, empty document translation results (ArtifactID: {id})", documentArtifactId.ToString());
                    return documentArtifactId;
                }

                // Log the translation result
                _logger.LogDebug("Azure Translator, translation check (ArtifactID: {id}, part: {part}, detected language: {language}, confidence score: {confidence}, length: {length})", documentArtifactId.ToString(), i.ToString(), translationResults[0].DetectedLanguage.Language, translationResults[0].DetectedLanguage.Score.ToString("0.00"), translationResults[0].Translations[0].Text.Length.ToString());
                // Get the translation result
                partsTranslated.Add(translationResults[0].Translations[0].Text);
            }

            // Construct translated text
            string textTranslated = string.Join(string.Empty, partsTranslated);
            Stream streamTranslated = new MemoryStream();
            StreamWriter streamWriter = new StreamWriter(streamTranslated);
            streamWriter.Write(textTranslated);
            streamWriter.Flush();
            streamTranslated.Position = 0;

            // Log translated document
            _logger.LogDebug("Azure Translator, translated document (ArtifactID: {id}, length: {length})", documentArtifactId.ToString(), textTranslated.Length.ToString());

            // Update document translated text
            try
            {
                // Construct objects for document update
                RelativityObjectRef relativityObject = new RelativityObjectRef
                {
                    ArtifactID = documentArtifactId
                };
                FieldRef relativityField = new FieldRef
                {
                    Name = destinationField
                };
                UpdateLongTextFromStreamRequest updateRequest = new UpdateLongTextFromStreamRequest
                {
                    Object = relativityObject,
                    Field = relativityField
                };
                KeplerStream keplerStream = new KeplerStream(streamTranslated);
                await objectManager.UpdateLongTextFromStreamAsync(workspaceId, updateRequest, keplerStream);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Azure Translator, document for translation update error (ArtifactID: {id})", documentArtifactId.ToString());
                return documentArtifactId;
            }
            
            // Return 0 as all went without error
            return 0;
        }
    }
}