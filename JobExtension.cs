using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography;
using Autodesk.Connectivity.Explorer.ExtensibilityTools;
using Autodesk.Connectivity.Extensibility.Framework;
using Autodesk.Connectivity.JobProcessor.Extensibility;
using Autodesk.Connectivity.WebServices;
using Autodesk.Connectivity.WebServicesTools;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Properties;
using static Autodesk.DataManagement.Client.Framework.Vault.Currency.CopyDesign.PropertyBehavior;
using ACET = Autodesk.Connectivity.Explorer.ExtensibilityTools;
using ACW = Autodesk.Connectivity.WebServices;


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
                UpdateItemProperties(context, job);
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
        private void UpdateItemProperties(IJobProcessorServices context, IJob job)
        {
            int fileId = int.Parse(job.Params["FileId"]); 
            Connection vaultConn = context.Connection;

            File file = GetFileById(fileId, vaultConn);
            Item item = GetItemByFileId(fileId, vaultConn);

            List<PropInstParamArray> propInstParamList = GetPropInstParamArray(job,vaultConn);
            Dictionary<PropDef, object> changedPropDefDict = GetChangedPropDef(job,vaultConn);

            UpdateItem(vaultConn, item, propInstParamList);
            UpdateFile(vaultConn, file, changedPropDefDict);


            AddUpadteItemLinksJob(file.Id, vaultConn);

        }
        private Dictionary<PropDef, object> GetChangedPropDef(IJob job, Connection vaultCon)
        {
            Dictionary<PropDef, object> changedPropDefDict = new Dictionary<PropDef, object>();
            foreach (var jobParam in job.Params)
            {
                PropDef propDef = null;
                if (jobParam.Key != "FileId")
                {
                    string propDefSysName = jobParam.Key;
                    propDef = GetPropDefBySysName(propDefSysName, vaultCon);
                    changedPropDefDict.Add(propDef, jobParam.Value);
                }
            }
            return changedPropDefDict;

        }
        private List<PropInstParamArray> GetPropInstParamArray(IJob job, Connection vaultCon)
        {
            
            List<PropInstParam> propInstParamItemsList = new List<PropInstParam>();
            foreach (var jobParam in job.Params)
            {
                PropDef propDef = null;
                if (jobParam.Key != "FileId")
                {
                    string propDefSysName = jobParam.Key;
                    propDef = GetPropDefBySysName(propDefSysName, vaultCon);

                    PropInstParam propParam = new PropInstParam();
                    propParam.PropDefId = propDef.Id;
                    propParam.Val = jobParam.Value;
                    propInstParamItemsList.Add(propParam);

                }
            }
            List<PropInstParamArray> propInstParamArray = new List<PropInstParamArray>()
            { 
                new PropInstParamArray() {
                    Items= propInstParamItemsList.ToArray()
                }
            };
            List<PropInstParamArray> propInstParamList = new List<PropInstParamArray>()
            {
                 new PropInstParamArray() {
                        Items= propInstParamItemsList.ToArray()
                 }
            };


            return propInstParamList;
        }

        private void UpdateItemLinks(IJobProcessorServices context, IJob job, List<PropInstParamArray> propInstParamList)
        {
            long fileId = long.Parse(job.Params["FileId"]);
            Connection vaultConnection = context.Connection;
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            ItemService itemService = webServiceManager.ItemService;

            Item item = itemService.GetItemsByFileId(fileId).First();

            try
            {
                var linkTypeOptions = ItemFileLnkTypOpt.Primary
                | ItemFileLnkTypOpt.PrimarySub
                | ItemFileLnkTypOpt.Secondary
                | ItemFileLnkTypOpt.SecondarySub
                | ItemFileLnkTypOpt.StandardComponent
                | ItemFileLnkTypOpt.Tertiary;
                var assocs = itemService.GetItemFileAssociationsByItemIds(
                    new long[] { item.Id }, linkTypeOptions);
                itemService.AddFilesToPromote(assocs.Select(x => x.CldFileId).ToArray(), ItemAssignAll.No, true);
                var promoteOrderResults = itemService.GetPromoteComponentOrder(out DateTime timeStamp);
                if (promoteOrderResults.PrimaryArray != null
                    && promoteOrderResults.PrimaryArray.Any())
                {
                    itemService.PromoteComponents(timeStamp, promoteOrderResults.PrimaryArray);
                }
                if (promoteOrderResults.NonPrimaryArray != null
                    && promoteOrderResults.NonPrimaryArray.Any())
                {
                    itemService.PromoteComponentLinks(promoteOrderResults.NonPrimaryArray);
                }
                var promoteResult = itemService.GetPromoteComponentsResults(timeStamp);

                Item[] items = promoteResult.ItemRevArray;

                vaultConnection.WebServiceManager.ItemService.UpdateItemProperties(new long[] { item.RevId }, propInstParamList.ToArray());

                itemService.UpdateAndCommitItems(items);

            }
            catch
            {
                itemService.UndoEditItems(new long[] { item.Id });
                throw;
            }

        }

        private void UpdateItem(Connection vaultConnection, Item item, List<PropInstParamArray> propInstParamList)
        {
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            ItemService itemService = webServiceManager.ItemService;

            try
            {
                //var linkTypeOptions = ItemFileLnkTypOpt.Primary
                //| ItemFileLnkTypOpt.PrimarySub
                //| ItemFileLnkTypOpt.Secondary
                //| ItemFileLnkTypOpt.SecondarySub
                //| ItemFileLnkTypOpt.StandardComponent
                //| ItemFileLnkTypOpt.Tertiary;
                //var assocs = itemService.GetItemFileAssociationsByItemIds(
                //    new long[] { item.Id }, linkTypeOptions);
                //itemService.AddFilesToPromote(assocs.Select(x => x.CldFileId).ToArray(), ItemAssignAll.No, true);
                //var promoteOrderResults = itemService.GetPromoteComponentOrder(out DateTime timeStamp);
                //if (promoteOrderResults.PrimaryArray != null
                //    && promoteOrderResults.PrimaryArray.Any())
                //{
                //    itemService.PromoteComponents(timeStamp, promoteOrderResults.PrimaryArray);
                //}
                //if (promoteOrderResults.NonPrimaryArray != null
                //    && promoteOrderResults.NonPrimaryArray.Any())
                //{
                //    itemService.PromoteComponentLinks(promoteOrderResults.NonPrimaryArray);
                //}

                //var promoteResult = itemService.GetPromoteComponentsResults(timeStamp);

                //Item[] items = promoteResult.ItemRevArray;

                //vaultConnection.WebServiceManager.ItemService.UpdateItemProperties(new long[] { item.RevId }, propInstParamList.ToArray());

                //itemService.UpdateAndCommitItems(items);

                itemService.UpdatePromoteComponents(new long[] { item.RevId },
                    ItemAssignAll.No, false);

                DateTime timestamp;

                GetPromoteOrderResults promoteOrder =

                    itemService.GetPromoteComponentOrder(out timestamp);

                itemService.PromoteComponents(timestamp, promoteOrder.PrimaryArray);

                ItemsAndFiles itemsAndFiles =
                    itemService.GetPromoteComponentsResults(timestamp);


                vaultConnection.WebServiceManager.ItemService.UpdateItemProperties(new long[] { item.RevId }, propInstParamList.ToArray());

                List<Item> items = itemsAndFiles.ItemRevArray
                        .Where((x, index) => itemsAndFiles.StatusArray[index] == 4)
                        .ToList();

                itemService.UpdateAndCommitItems(items.ToArray());


            }
            catch (Exception ex)
            {
                itemService.UndoEditItems(new long[] { item.Id });
                throw ex;
            }
        }

        private void UpdateFile(Connection vaultConn, File file, Dictionary<PropDef, object> changedPropDefDict)
        {
            ACET.IExplorerUtil mExplUtil = ExplorerLoader.LoadExplorerUtil(
            vaultConn.Server, vaultConn.Vault, vaultConn.UserID, vaultConn.Ticket);
            mExplUtil.UpdateFileProperties(file, changedPropDefDict);
        }
        private void AddUpadteItemLinksJob(long fileId, Connection vaultCon)
        {
            JobParam[] jobParams = new JobParam[]
            { new JobParam()
            {
                Name = "FileId",
                    Val = fileId.ToString()
                }  
            };
            File file = GetFileById(fileId, vaultCon);
            vaultCon.WebServiceManager.JobService.AddJob("KRATKI.UpdateItemLinks", $"KRATKI.UpdateItemLinks: {file.Name}", jobParams, 10);

        }

        private File GetFileById(long fileId, Connection vaultConnection)
        {
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            DocumentService docService = webServiceManager.DocumentService; 

            File file = docService.GetFileById(fileId);

            return file;
        }

        private Item GetItemByFileId(int fileId, Connection vaultConnection)
        {
            WebServiceManager webServiceManager = vaultConnection.WebServiceManager;
            ItemService itemService = webServiceManager.ItemService;

            Item item = itemService.GetItemsByFileId(fileId).First();

            return item;
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
