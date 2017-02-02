using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Elision.Foundation.Kernel;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.SecurityModel;
using Sitecore.Web;
using Sitecore.Workflows;
using Sitecore.Workflows.Simple;

namespace Elision.Feature.Library.Workflows
{
    public class ExecuteCommandOnRenderingDatasources
    {
        public void Process(WorkflowPipelineArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (args.DataItem == null)
                return;

            var parameters = WebUtil.ParseUrlParameters(args.ProcessorItem.InnerItem["parameters"]);
            var options = BuildOptions(parameters);

            using (new SecurityDisabler())
            {
                var pageComponents = GetAllComponentItems(args.DataItem);

                foreach (var pageComponent in pageComponents)
                    CommandDatasourceItem("Auto-" + options.Type + " with " + args.DataItem.DisplayName, pageComponent, options);
            }
        }

        private void CommandDatasourceItem(string message, Item componentItem, CommandRenderingDatasourceOptions options)
        {
            var workflow = componentItem.State.GetWorkflow();
            if (!ItemInExpectedWorkflowState(componentItem, workflow, options)) 
                return;

            workflow.Execute(options.CommandId.ToString(), componentItem, message, false);
        }

        protected virtual bool ItemInExpectedWorkflowState(Item componentItem, IWorkflow workflow, CommandRenderingDatasourceOptions options)
        {
            if (workflow == null || workflow.WorkflowID != options.WorkflowId.ToString())
                return false;

            var workflowState = componentItem.State.GetWorkflowState();
            if (workflowState == null || workflowState.FinalState)
                return false;

            var commandItem = componentItem.Database.GetItem(options.CommandId);
            return commandItem != null && commandItem.Parent.ParentID == options.WorkflowId;
        }

        protected virtual CommandRenderingDatasourceOptions BuildOptions(NameValueCollection parameters)
        {
            var options = new CommandRenderingDatasourceOptions();

            ComponentWorkflowAction type;
            options.Type = Enum.TryParse(parameters["type"], true, out type) ? type : ComponentWorkflowAction.Approve;

            if (string.IsNullOrWhiteSpace(parameters["commandid"]) || !ID.IsID(parameters["commandid"]))
            {
                switch (options.Type)
                {
                    case ComponentWorkflowAction.Submit:
                        options.CommandId = WorkflowIDs.SubmitCommand;
                        break;
                    case ComponentWorkflowAction.Reject:
                        options.CommandId = WorkflowIDs.RejectCommand;
                        break;
                    case ComponentWorkflowAction.Approve:
                        options.CommandId = WorkflowIDs.ApproveCommand;
                        break;
                    default:
                        options.CommandId = ID.Null;
                        break;
                }
            }
            else
            {
                options.CommandId = ID.Parse(parameters["commandid"]);
            }

            if (!string.IsNullOrWhiteSpace(parameters["workflowid"]) && ID.IsID(parameters["workflowid"]))
                options.WorkflowId = ID.Parse(parameters["workflowid"]);
            else
                options.WorkflowId = WorkflowIDs.RelatedItemWorkflow;

            return options;
        }

        protected virtual IEnumerable<Item> GetAllComponentItems(Item item)
        {
            var layoutParsed = item.GetLayoutDefinition();

            var devices = layoutParsed.Devices.Cast<DeviceDefinition>();
            var renderings = devices.SelectMany(d => d.Renderings.Cast<RenderingDefinition>());
            var datasources = renderings.Select(GetRenderingDatasource)
                                        .Where(ds => !string.IsNullOrEmpty(ds))
                                        .ToArray();
            return datasources.Select(ds => item.Database.ResolveDatasource(ds, item))
                              .Where(dataSourceItem => dataSourceItem != null)
                              .SelectMany(x => x.TemplateName == "Form"
                                                   ? x.Axes.GetDescendants().Union(new[] {x})
                                                   : new[] {x})
                              .ToArray();
        }

        protected virtual string GetRenderingDatasource(RenderingDefinition r)
        {
            if (!string.IsNullOrWhiteSpace(r.Datasource))
                return r.Datasource;

            var prop = r.DynamicProperties?.FirstOrDefault(x => x.Name == "s:ds" || x.Name == "ds");
            return prop?.Value;
        }

        protected class CommandRenderingDatasourceOptions
        {
            public ComponentWorkflowAction Type { get; set; }
            public ID CommandId { get; set; }
            public ID WorkflowId { get; set; }
        }

        protected enum ComponentWorkflowAction
        {
            Submit,
            Approve,
            Reject
        }
    }
}