//-----------------------------------------------------------------------
// <copyright file="IndexMapper.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Query
{
    using System;

    using Indexer;
    using WebApi;

    /// <summary>
    /// IndexMapper returns an index for a given scope (account/collection/project/repo).
    /// </summary>
    public class IndexMapper
    {
        private readonly IQueryIndexMapper m_queryIndexMapper;

        public IndexMapper(IQueryIndexMapper queryIndexMapper)
        {
            m_queryIndexMapper = queryIndexMapper;
        }

        /// <summary>
        /// Returns the index corresponding to the scope and filters in the input query.
        /// </summary>
        /// <param name="searchRequest"> The input query request. </param>
        /// <returns> Index. </returns>
        public string GetIndex(SearchQuery searchRequest)
        {
            if (string.IsNullOrEmpty(searchRequest.Scope))
            {
                throw new ArgumentException("Scope");
            }
            
            // TODO : shga [25/07/14] - Parse the filters for collection/proj/repo.
            return m_queryIndexMapper.GetAccountIndex(searchRequest.Scope);
        }
    }
}
