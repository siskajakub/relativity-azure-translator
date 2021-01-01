using System.Collections.Generic;
using System.Data.SqlClient;

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
            _logger.LogDebug("Current Workspace ID: {workspaceId}", workspaceId.ToString());

            // Check if all Instance Settings are in place
            IDictionary<string, string> instanceSettings = this.GetInstanceSettings(ref response, new string[] { "SubscriptionKey", "Endpoint", "SourceSqlField", "DestinationSqlField", "Cost1MCharacters" });
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
                string sqlText = "SELECT SUM(LEN(CAST([" + instanceSettings["SourceSqlField"] + "] AS NVARCHAR(MAX)))) FROM [EDDSDBO].[Document] AS [Document] JOIN [Resource].[" + this.MassActionTableName + "] AS [MassActionTableName] ON [Document].[ArtifactID] = [MassActionTableName].[ArtifactID]";
                _logger.LogDebug("Azure Translator translation cost SQL Parameter and Query: {query}", sqlText);
                long count = (long)this.Helper.GetDBContext(workspaceId).ExecuteSqlStatementAsScalar(sqlText);

                // Calculate translation cost
                float cost = (count / 1000000f) * float.Parse(instanceSettings["Cost1MCharacters"]);
                _logger.LogDebug("Azure Translator translation cost: {price}CHF", cost.ToString("0.00"));
                response.Message = "Translation cost is " + cost.ToString("0.00") + "CHF";
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Azure Translator translation cost error ({SourceSqlField}, {Cost1MCharacters})", instanceSettings["SourceSqlField"], instanceSettings["Cost1MCharacters"]);

                response.Success = false;
                response.Message = "Azure Translator translation cost error";
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
            _logger.LogDebug("Current Workspace ID: {workspaceId}", workspaceId.ToString());

            // Display general status
            this.ChangeStatus("Translating documents");

            // Iterate through documents and translate each of them
            foreach (int artifactId in this.BatchIDs) {
                // TODO: translate document in Azure and update Relativity using Object Manager API
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
            foreach(string name in instanceSettingsNames)
            {
                try
                {
                    instanceSettingsValues.Add(name, this.Helper.GetInstanceSettingBundle().GetString("Azure.Translator", name));
                    if (instanceSettingsValues[name].Length <= 0)
                    {
                        _logger.LogError("Instance Settings error: {section}/{name}", "Azure.Translator", name);

                        response.Success = false;
                        response.Message = "Instance Settings error";
                        return instanceSettingsValues;
                    }
                }
                catch (System.Exception e)
                {
                    _logger.LogError(e, "Instance Settings error: {section}/{name}", "Azure.Translator", name);

                    response.Success = false;
                    response.Message = "Instance Settings error";
                    return instanceSettingsValues;
                }
                _logger.LogDebug("Azure Translator Instance Setting: {name}=>{value}", name, instanceSettingsValues[name]);
            }

            return instanceSettingsValues;
        }
    }
}