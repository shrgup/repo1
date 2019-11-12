//-----------------------------------------------------------------------
// <copyright file="SearchQueryForwarder.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Query
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    using Common;
    using Common.Arriba.Expressions;
    using Platforms.SearchEngine.Definitions;
    using Platforms.SearchEngine.Implementations;
    using Server.Telemetry;
    using Server.Telemetry.Implementations;
    using WebApi;

    /// <summary>
    /// Prepares a platform specific search request from the input query and 
    /// forwards it to the search platform.
    /// </summary>
    public class SearchQueryForwarder
    {
        private readonly ISearchPlatform m_searchPlatform;

        public SearchQueryForwarder(string searchPlatformConnectionString)
        {
            if (string.IsNullOrEmpty(searchPlatformConnectionString))
            {
                throw new ArgumentException("searchPlatformConnectionString");
            }

            if (!Uri.IsWellFormedUriString(searchPlatformConnectionString, UriKind.Absolute))
            {
                throw new ArgumentOutOfRangeException("searchPlatformConnectionString");
            }

            m_searchPlatform = SearchPlatformFactory.Create(searchPlatformConnectionString);
        }

        protected SearchQueryForwarder(ISearchPlatform searchPlatform)
        {
            m_searchPlatform = searchPlatform;
        }

        /// <summary>
        /// Transforms the input query string into platform specific query expression,
        /// creates a platform specific search request containing the query expression and 
        /// forwards it to the search platform.
        /// </summary>
        /// <param name="searchQuery">The search query</param>
        /// <param name="indexName">Index to search</param>
        /// <returns>Query response containing search results</returns>
        public CodeQueryResponse ForwardSearchRequest(SearchQuery searchQuery, string indexName)
        {
            Tracer.TraceEnter(QueryPipelineTracePoints.SearchQueryForwarderStart, TraceArea.Query, TraceLayer.Query, "ForwardSearchRequest");

            if (searchQuery == null)
            {
                throw new ArgumentNullException("searchQuery");
            }

            if (string.IsNullOrWhiteSpace(indexName))
            {
                throw new ArgumentException("Index name should not be null or contain only whitespaces", "indexName");
            }

            var platformSearchRequest = new SearchQueryRequest(
                indexName: indexName,
                queryParseTree: SearchQueryTransformer.GetQueryParseTree(searchQuery),
                searchFilters: SearchQueryTransformer.GetProjRepoFilters(searchQuery),
                skipResults: searchQuery.SkipResults,
                takeResults: searchQuery.TakeResults,
                fields: new List<string>
                {
                    CommonConstants.FilePathField,
                    CommonConstants.AccountNameField,
                    CommonConstants.CollectionNameField,
                    CommonConstants.ProjectNameField,
                    CommonConstants.RepoNameField,
                    CommonConstants.BranchNameField,
                    CommonConstants.CommitIdField,
                    CommonConstants.ContentIdField,
                    CommonConstants.FileExtensionField
                },
                contractType: CommonConstants.SourceNoDedupeFileContract,
                highlightField: CommonConstants.ContentField,
                searchScope: new List<string> { searchQuery.Scope });

            CodeQueryResponse searchResponse = null;

            if (!(platformSearchRequest.QueryParseTree is EmptyExpression))
            {
                var platformSearchResponse = m_searchPlatform.Search<FileContract>(platformSearchRequest);
                searchResponse = PrepareSearchResponse(platformSearchResponse);
            }
            else
            {
                searchResponse = new CodeQueryResponse
                {
                    Results = new CodeResults(count: 0, values: Enumerable.Empty<CodeResult>()),
                    FilterCategories = Enumerable.Empty<FilterCategory>()
                };

                Tracer.TraceInfo(
                    QueryPipelineTracePoints.SearchQueryForwarderEmptyExpression,
                    TraceArea.Query,
                    TraceLayer.Query,
                    string.Format(CultureInfo.InvariantCulture, "Search string [{0}] resulted into an empty query. Empty results will be returned", searchQuery.SearchText));
            }

            searchResponse.Query = searchQuery;

            Tracer.TraceLeave(QueryPipelineTracePoints.SearchQueryForwarderEnd, TraceArea.Query, TraceLayer.Query, "ForwardSearchRequest");

            return searchResponse;
        }

        private CodeQueryResponse PrepareSearchResponse(ISearchQueryResponse platformSearchResponse)
        {
            if (platformSearchResponse == null)
            {
                throw new ArgumentNullException("Search platform response is null");
            }

            if (platformSearchResponse.Facets == null)
            {
                throw new ArgumentNullException("Facets returned by Search Platform is null");
            }

            if (platformSearchResponse.Results == null)
            {
                throw new Common.SearchException("Results returned by Search Platform is null");
            }

            var codeResultsList = new List<CodeResult>();

            foreach (var result in platformSearchResponse.Results)
            {
                string filePath = string.Empty;
                string accountName = string.Empty;
                string collectionName = string.Empty;
                string projectName = string.Empty;
                string repositoryName = string.Empty;
                string branchName = string.Empty;
                string fileName = string.Empty;

                if (!result.Fields.TryGetValue(CommonConstants.FilePathField, out filePath))
                {
                    throw new Common.SearchException("Search Platform Response: File path not found");
                }

                if (!result.Fields.TryGetValue(CommonConstants.AccountNameField, out accountName))
                {
                    throw new Common.SearchException("Search Platform Response: Account name not found");
                }

                if (!result.Fields.TryGetValue(CommonConstants.CollectionNameField, out collectionName))
                {
                    throw new Common.SearchException("Search Platform Response: Collection name not found");
                }

                if (!result.Fields.TryGetValue(CommonConstants.ProjectNameField, out projectName))
                {
                    throw new Common.SearchException("Search Platform Response: Project name not found");
                }

                if (!result.Fields.TryGetValue(CommonConstants.RepoNameField, out repositoryName))
                {
                    throw new Common.SearchException("Search Platform Response: Repository name not found");
                }

                if (!result.Fields.TryGetValue(CommonConstants.BranchNameField, out branchName))
                {
                    throw new Common.SearchException("Search Platform Response: Branch name not found");
                }

                int hitCount = result.HitCount;
                var codeHits = new List<Hit>();

                foreach (var hit in result.Hits)
                {
                    var codeHit = new Hit { CharOffset = hit.CharOffset, Length = hit.Length };
                    codeHits.Add(codeHit);
                }

                // TODO: NeMakam [28/07/14] Add support for contentId, commitId and fileExtension
                var codeResult = new CodeResult(
                    filename: Path.GetFileName(filePath),
                    hitCount: hitCount,
                    path: filePath,
                    hits: codeHits,
                    account: accountName,
                    collection: collectionName,
                    project: projectName,
                    repository: repositoryName,
                    version: branchName);

                codeResultsList.Add(codeResult);
            }

            var filterCategories = platformSearchResponse.Facets.Select(f => new FilterCategory { Name = f.Key, Filters = f.Value });

            var codeQueryResponse = new CodeQueryResponse
            {
                FilterCategories = filterCategories,
                Results = new CodeResults(platformSearchResponse.TotalMatches, codeResultsList)
            };

            return codeQueryResponse;
        }
    }
}