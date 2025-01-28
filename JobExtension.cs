using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Connectivity.Explorer.ExtensibilityTools;
using Autodesk.Connectivity.Extensibility.Framework;
using Autodesk.Connectivity.JobProcessor.Extensibility;
using Autodesk.Connectivity.WebServices;
using Autodesk.Connectivity.WebServicesTools;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections;
using ACET = Autodesk.Connectivity.Explorer.ExtensibilityTools;

[assembly: ApiVersion("15.0")]
[assembly: ExtensionId("48a44c92-f5c8-4d70-9a1c-0693073f8cc1")]


namespace UpdateProperties
{
    public class JobExtension : IJobHandler
    {
        private static string JOB_TYPE = "KRATKI.UpdateProperties";

        #region IJobHandler Implementation
        public bool CanProcess(string jobType)
        {
            return jobType == JOB_TYPE;
        }

        public JobOutcome Execute(IJobProcessorServices context, IJob job)
        {
            try
            {
                UpdateFileProperties(context, job);
                return JobOutcome.Success;
            }
            catch (Exception ex)
            {
                context.Log(ex, "Nie uda³o siê zaktualizowaæ w³aœciwoœci: " + ex.ToString() + " ");
                return JobOutcome.Failure;
            }
        }

        public void OnJobProcessorShutdown(IJobProcessorServices context)
        {
            //throw new NotImplementedException();
        }

        public void OnJobProcessorSleep(IJobProcessorServices context)
        {
            //throw new NotImplementedException();
        }

        public void OnJobProcessorStartup(IJobProcessorServices context)
        {
            //throw new NotImplementedException();
        }

        public void OnJobProcessorWake(IJobProcessorServices context)
        {
            //throw new NotImplementedException();
        }
        #endregion IJobHandler Implementation
        //private void UpdateFileProperties(File file, Dictionary<ACW.PropDef, object> mPropDictonary)
        private void UpdateFileProperties(IJobProcessorServices context, IJob job)
        {
            int fileId = int.Parse(job.Params["FileId"]); 
            Connection vaultCon = context.Connection;

            File file = GetFileById(fileId, vaultCon);

            
            Dictionary<PropDef,object> changedPropDefDict = new Dictionary<PropDef, object>() ;   

            
            foreach(var jobParam in job.Params)
            {
                PropDef propDef = null;
                if (jobParam.Key != "FileId")
                {
                    string propDefSysName = jobParam.Key;
                    propDef = GetPropDefBySysName(propDefSysName, vaultCon);
                    changedPropDefDict.Add(propDef, jobParam.Value);
                }
            }

            ACET.IExplorerUtil mExplUtil = ExplorerLoader.LoadExplorerUtil(
            context.Connection.Server, context.Connection.Vault, context.Connection.UserID, context.Connection.Ticket);

            mExplUtil.UpdateFileProperties(file, changedPropDefDict);

            AddJob(fileId, context.Connection);

        }

        private void AddJob(int fileId, Connection vaultCon)
        {
            JobParam[] jobParams = new JobParam[] { };

            jobParams[0] = new JobParam()
            {
                Name = "FileId",
                Val = fileId.ToString()
            };

            File file = GetFileById(fileId, vaultCon);
            vaultCon.WebServiceManager.JobService.AddJob("KRATKI.UpdateItemLinks", $"KRATKI.UpdateItemLinks: {file.Name}", jobParams, 10);

        }

        private File GetFileById(int fileId, Connection vaultConnection)
        {
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            DocumentService docService = webServiceManager.DocumentService; 

            File file = docService.GetFileById(fileId);

            return file;
        }

        private PropDef GetPropDefBySysName(string propDefSysName, Connection vaultConnection)
        {
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            PropertyService propertyService = webServiceManager.PropertyService;

            PropDef propDef = propertyService.FindPropertyDefinitionsBySystemNames("FILE", new string[] { propDefSysName }).First();

            return propDef;
        }

    }
}
