﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CSS.Core;
using Microsoft.CSS.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace MadsKristensen.EditorExtensions.Html
{
    class ResultSource : IPeekResultSource
    {
        private readonly HtmlDefinitionPeekItem peekableItem;

        public ResultSource(HtmlDefinitionPeekItem peekableItem)
        {
            this.peekableItem = peekableItem;
        }

        public void FindResults(string relationshipName, IPeekResultCollection resultCollection, CancellationToken cancellationToken, IFindPeekResultsCallback callback)
        {
            if (relationshipName != PredefinedPeekRelationships.Definitions.Name)
            {
                return;
            }

            RuleSet rule;
            string file = FindRuleSetInFile(new[] { ".less", ".scss", ".css" }, peekableItem._className, out rule);

            if (rule == null)
            {
                callback.ReportProgress(1);
                return;
            }

            var displayInfo = new PeekResultDisplayInfo(label: peekableItem._className, labelTooltip: file, title: Path.GetFileName(file), titleTooltip: file);

            var result = peekableItem._peekResultFactory.Create
            (
                displayInfo,
                file,
                new Span(rule.Start, rule.Length),
                rule.Start,
                false
            );

            resultCollection.Add(result);
            callback.ReportProgress(1);
        }

        private string FindRuleSetInFile(IEnumerable<string> extensions, string className, out RuleSet rule)
        {
            string root = ProjectHelpers.GetProjectFolder(peekableItem._textbuffer.GetFileName());
            string result = null;
            rule = null;

            foreach (string ext in extensions)
            {
                ICssParser parser = CssParserLocator.FindComponent(Mef.GetContentType(ext.Trim('.'))).CreateParser();

                foreach (string file in Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".min" + ext, StringComparison.OrdinalIgnoreCase) ||
                        file.Contains("node_modules") ||
                        file.Contains("bower_components"))
                        continue;

                    string text = FileHelpers.ReadAllTextRetry(file).ConfigureAwait(false).GetAwaiter().GetResult();
                    int index = text.IndexOf("." + className, StringComparison.Ordinal);

                    if (index > -1)
                    {
                        var css = parser.Parse(text, true);
                        var visitor = new CssItemCollector<ClassSelector>(false);
                        css.Accept(visitor);

                        var selectors = visitor.Items.Where(c => c.ClassName.Text == className);
                        var high = selectors.FirstOrDefault(c => c.FindType<AtDirective>() == null && (c.Parent.NextSibling == null || c.Parent.NextSibling.Text == ","));

                        if (high != null)
                        {
                            rule = high.FindType<RuleSet>();
                            return file;
                        }

                        var medium = selectors.FirstOrDefault(c => c.Parent.NextSibling == null || c.Parent.NextSibling.Text == ",");

                        if (medium != null)
                        {
                            rule = medium.FindType<RuleSet>();
                            result = file;
                            continue;
                        }

                        var low = selectors.FirstOrDefault();

                        if (low != null)
                        {
                            rule = low.FindType<RuleSet>();
                            result = file;
                            continue;
                        }
                    }
                }
            }

            return result;
        }
    }
}