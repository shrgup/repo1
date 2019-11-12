//-----------------------------------------------------------------------
// <copyright file="SearchQueryTransformer.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Query
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;

    using Arriba.Correctors;
    using Arriba.Model;
    using Common;
    using Common.Arriba.Expressions;
    using Extensions;
    using Server.Telemetry;
    using Server.Telemetry.Implementations;
    using WebApi;

    /// <summary>
    /// Transforms the input query request into canonical form.
    /// </summary>
    public static class SearchQueryTransformer
    {
        /// <summary>
        /// Transforms query request into canonical form
        /// </summary>
        /// <param name="searchQuery">Input search request</param>
        /// <returns>Parse tree representation of the input query</returns>
        public static IExpression GetQueryParseTree(SearchQuery searchQuery)
        {
            if (searchQuery.SearchText == null)
            {
                throw new ArgumentNullException("searchQuery.SearchText");
            }

            if (searchQuery.Filters == null)
            {
                throw new ArgumentNullException("searchQuery.Filters");
            }

            Tracer.TraceVerbose(
                QueryPipelineTracePoints.SearchQueryTransformerInputQueryString,
                TraceArea.Query,
                TraceLayer.Query,
                string.Format(CultureInfo.InvariantCulture, "Input search query: {0}", searchQuery.Text()));

            var sanitizedSearchString = Sanitize(searchQuery.SearchText);
            var queryParseTree = QueryParser.Parse(sanitizedSearchString);
            var correctedQueryParseTree = Correct(queryParseTree, searchQuery.Filters);

            if (correctedQueryParseTree is EmptyExpression)
            {
                Tracer.TraceWarning(
                    QueryPipelineTracePoints.SearchQueryTransformerEmptyExpression,
                    TraceArea.Query,
                    TraceLayer.Query,
                    string.Format(CultureInfo.InvariantCulture, "Search string [{0}] resulted into an empty query", searchQuery.SearchText));
            }

            return correctedQueryParseTree;
        }

        /// <summary>
        /// Extracts project and repository filters (if present) from search query
        /// </summary>
        /// <param name="searchQuery">Input search query</param>
        /// <returns>Project and repository filters</returns>
        public static IDictionary<string, IEnumerable<string>> GetProjRepoFilters(SearchQuery searchQuery)
        {
            if (searchQuery == null)
            {
                throw new ArgumentNullException("searchQuery");
            }

            if (searchQuery.Filters == null)
            {
                throw new ArgumentNullException("searchQuery.Filters");
            }

            var searchFilters = new Dictionary<string, IEnumerable<string>>();
            foreach (var filterCategory in searchQuery.Filters)
            {
                switch (filterCategory.Name)
                {
                    case CodeSearchFilters.ProjectFilterId:
                        searchFilters.Add(CommonConstants.ProjectNameField, filterCategory.Values.Select(s => s.NormalizeString()));
                        break;

                    case CodeSearchFilters.RepositoryFilterId:
                        searchFilters.Add(CommonConstants.RepoNameField, filterCategory.Values.Select(s => s.NormalizeString()));
                        break;

                    default:
                        Tracer.TraceWarning(
                            QueryPipelineTracePoints.SearchQueryTransformerUnidentifiedFilter,
                            TraceArea.Query,
                            TraceLayer.Query,
                            string.Format(CultureInfo.InvariantCulture, "Unrecognized filter [{0}] has been ignored", filterCategory.Name));
                        break;
                }
            }

            return searchFilters;
        }

        /// <summary>
        /// Replace all characters except the below with a space:
        ///     Word characters: 0-9, a-z, A-Z
        ///     Whitespaces
        ///     Asterisks
        ///     Double inverted quotes
        ///     Question mark
        ///     Forward slash
        ///     Backward slash
        ///     Colon
        ///     Left parentheses
        ///     Right parentheses
        ///     Greater Than operator
        ///     Less Than operator
        ///     Equals operator
        /// Path expressions are handled in a special manner
        /// </summary>
        /// <param name="searchString">Input search string</param>
        /// <returns>Search string stripped off of unsupported characters</returns>
        private static string Sanitize(string searchString)
        {
            // TODO: Here extensive use of Regex is to eliminate pruning of Path expressions
            // Since regex are inefficient, this needs to be refactored to have expression specific elimination
            // The idea in below is to match and store the path expression beforehand and then cleanse the string and then 
            // attach the path expressions back to the search string
            if (string.IsNullOrWhiteSpace(searchString))
            {
                return string.Empty;
            }

            const string pathWithinQuotesPattern = @"\""(\w|\W)*\s*path\s*:\s*[^""\\]*(?:\\.[^""\\]*)*\b\s*\"""; // e.g. "path: foo bar", "abc path : xyz " 
            const string pathExpressionPattern = @"path\s*:\s*([a-zA-Z0-9\/\\.@#\\$%&;`~!,=_'\\+\\(\\)\]\\[\\{\\}\\-]+|""[\sa-zA-Z0-9\/\\.@#\\$%&;`~!,=_'\\+\\(\\)\]\\[\\{\\}\\-]+"")";
            const string unwantedCharsRemoverExpressionPattern = @"[^\w\s\*\""\?\:\(\)]";

            const string uniqueIdForPathWithinQuotes = "8f8b8e1f02cc45ae94bad12c72575ac6";
            const string uniqueIdForPathExpressions = "e6b8128e4a3f47c5b7454a510c7aff53";
            const string replacementString = " ";

            // Remove paths within quotes
            MatchCollection pathWithinQuotesMatches = Regex.Matches(searchString, pathWithinQuotesPattern, RegexOptions.IgnoreCase);
            if (pathWithinQuotesMatches.Count > 0)
            {
                searchString = Regex.Replace(searchString, pathWithinQuotesPattern, uniqueIdForPathWithinQuotes, RegexOptions.IgnoreCase);
            }

            // Remove paths
            MatchCollection pathExpressionMatches = Regex.Matches(searchString, pathExpressionPattern, RegexOptions.IgnoreCase);
            if (pathExpressionMatches.Count > 0)
            {
                searchString = Regex.Replace(searchString, pathExpressionPattern, uniqueIdForPathExpressions, RegexOptions.IgnoreCase);
            }

            // Add back path within quotes
            for (int i = 0; i < pathWithinQuotesMatches.Count; i++)
            {
                searchString = searchString.Replace(uniqueIdForPathWithinQuotes, pathWithinQuotesMatches[i].Value);
            }

            // Remove unwanted chars
            searchString = Regex.Replace(searchString, unwantedCharsRemoverExpressionPattern, replacementString);

            // Add back paths
            const string colonWithQuotes = ":\"";
            const string colonWithoutQuotesPattern = ":\\s*";
            const string colonWithQuotesPattern = colonWithoutQuotesPattern + "\"";
            Regex pathReplacementRegex = new Regex(uniqueIdForPathExpressions);
            Regex colonWithQuotesRegex = new Regex(colonWithQuotesPattern);
            Regex colonWithoutQuotesRegex = new Regex(colonWithoutQuotesPattern, RegexOptions.IgnoreCase);

            for (int i = 0; i < pathExpressionMatches.Count; i++)
            {
                const int indexForCompleteExpressionMatch = 0;
                string pathExpressionToReplace = pathExpressionMatches[i].Groups[indexForCompleteExpressionMatch].Value;
                if (colonWithQuotesRegex.Matches(pathExpressionToReplace).Count == 0)
                {
                    // If path: is the expression, replacing it with path:" 
                    // so that query parser does not clean the search content
                    pathExpressionToReplace = colonWithoutQuotesRegex.Replace(pathExpressionToReplace, colonWithQuotes);
                    searchString = pathReplacementRegex.Replace(
                        input: searchString,
                        replacement: string.Format(CultureInfo.InvariantCulture, "{0}\"", pathExpressionToReplace),
                        count: 1);
                }
                else
                {
                    searchString = pathReplacementRegex.Replace(searchString, pathExpressionToReplace, count: 1);
                }
            }

            return searchString;
        }

        /// <summary>
        /// Converts a TermExpression object to CodeElementTermExpression wherever applicable
        /// </summary>
        /// <param name="queryParseTree">IExpression representing the query</param>
        /// <param name="searchFilterCollection">Code Element filters to be applied on the input query</param>
        /// <returns>Code element corrected query parse tree</returns>
        private static IExpression Correct(IExpression queryParseTree, IEnumerable<SearchFilter> searchFilterCollection)
        {
            var codeElementFilters = searchFilterCollection
                .Where(f => f.Name.Equals(CodeSearchFilters.CodeElementFilterId, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Values).FirstOrDefault() ?? Enumerable.Empty<string>();

            queryParseTree = CorrectorTraverser.CorrectTerms(queryParseTree, new CodeElementTermCorrector(codeElementFilters));

            return queryParseTree;
        }
    }
}
