using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Relativity.API;
using Relativity.Kepler.Transport;
using Relativity.Services.Objects;
using Relativity.Services.Objects.DataContracts;

namespace RelativityAzureTranslator
{
    [kCura.EventHandler.CustomAttributes.Description("Relativity Azure Translator")]
    [System.Runtime.InteropServices.Guid("7db432d7-09c1-44c9-8330-8a1f3ef28849")]
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
            IDictionary<string, string> instanceSettings = this.GetInstanceSettings(ref response, new string[] { "SubscriptionKey", "Endpoint", "SourceField", "DestinationField", "Cost1MCharacters" });
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
                _logger.LogDebug("Azure Translator, translation cost: {price}CHF", cost.ToString("0.00"));
                response.Message = "Translation cost is " + cost.ToString("0.00") + "CHF";
            }
            catch (System.Exception e)
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
            IDictionary<string, string> instanceSettings = this.GetInstanceSettings(ref response, new string[] { "SubscriptionKey", "Endpoint", "SourceField", "DestinationField", "Cost1MCharacters" });
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
                translationTasks.Add(TranslateDocument(workspaceId, this.BatchIDs[i], instanceSettings["SourceField"], instanceSettings["DestinationField"]));

                // Update progreass bar
                this.IncrementCount(1);

                // Allow only certain number of tasks to run concurently
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
            _logger.LogDebug("Azure Translator, waiting for all documents finish translating ({n} document(s))", this.BatchIDs.Count);
            Task.WaitAll(translationTasks.ToArray());

            // Update general status
            this.ChangeStatus("Checking the results of the document translation");

            // Check results
            List<string> translationErrors = new List<string>();
            for (int i = 0; i < translationTasks.Count; i++)
            {
                // If translation was not done add to the error List
                _logger.LogDebug("Azure Translator, translation task result: {result} (task: {task})", translationTasks[i].Result, translationTasks[i].Id);
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
                        _logger.LogError("Azure Translator, Instance Settings error: {section}/{name}", "Azure.Translator", name);

                        response.Success = false;
                        response.Message = "Instance Settings error";
                        return instanceSettingsValues;
                    }
                }
                catch (System.Exception e)
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
        private async Task<int> TranslateDocument(int workspaceId, int documentArtifactId, string sourceField, string destinationField)
        {
            // Get logger
            Relativity.API.IAPILog _logger = this.Helper.GetLoggerFactory().GetLogger().ForContext<MassOperationHandler>();

            // Get Relativity Object Manager API
            IObjectManager objectManager = this.Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.CurrentUser);

            // Get document text
            Stream stream;
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
                stream = await keplerStream.GetStreamAsync();
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Azure Translator, document for translation retrieval error");

                return documentArtifactId;
            }

            // Copy stream to new stream as old does not support seeking
            MemoryStream memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            stream.Dispose();

            // Log original document
            _logger.LogDebug("Azure Translator, original document (ArtifactID: {id}, length: {length})", documentArtifactId, memoryStream.Length);

            // TODO: implement actual translation call

            // Log translated document
            _logger.LogDebug("Azure Translator, translated document (ArtifactID: {id}, length: {length})", documentArtifactId, memoryStream.Length);

            // Update document translated text
            try
            {
                // reset MemoryStream position
                memoryStream.Position = 0;

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
                KeplerStream keplerStream = new KeplerStream(memoryStream);
                await objectManager.UpdateLongTextFromStreamAsync(workspaceId, updateRequest, keplerStream);
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Azure Translator, document for translation update error");

                return documentArtifactId;
            }

            // Return 0 as all went without error
            return 0;
        }
    }
}