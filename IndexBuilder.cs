//-----------------------------------------------------------------------
// <copyright file="IndexBuilder.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Indexer
{
    using System;
    using System.Collections.Generic;

    using Microsoft.VisualStudio.Services.Search.Extensions.Libraries.reSearch.Core.Stores.ObjectStore;
    using Microsoft.VisualStudio.Services.Search.Extensions.Libraries.reSearch.Core.Stores.TreeStore;

    internal sealed class IndexBuilder : IIndexBuilder
    {
        private readonly IFileContracter m_fileContracter;
        private readonly IIndexMapper m_indexMapper;
        private readonly IndexBuilderParams m_indexBuilderParams;
        private string m_IndexName;
        private bool m_indexInitialized;

        /// <summary>
        /// Constructor for build index.
        /// Based on BuildIndexParams objects of different internal objects will be created.
        /// </summary>
        /// <param name="provisioner">Provisioner object</param>
        /// <param name="contracter">Contracter object</param>
        /// <param name="indexMapper">Index Mapper object</param>
        /// <param name="buildIndexParams">Parameters for building the index</param>

        public IndexBuilder(IFileContracter contracter,
                            IIndexMapper indexMapper,
                            IndexBuilderParams indexBuilderParams)
        {
            m_indexInitialized = false;
            m_IndexName = indexBuilderParams.Index;
            m_fileContracter = contracter;
            // TODO: [Satish - 7/22/2014] Pass the correct mapper from feeder service appropriately.
            m_indexMapper = indexMapper;
            m_indexBuilderParams = indexBuilderParams;
        }

        /// <summary>
        /// Get the index name in which docs will be fed.
        /// </summary>
        /// <param name="indexParams">Parameters to build index from like index provision scheme, index mapper scheme etc</param>
        public string GetIndexForFeeding()
        {
            string branchAlias = m_indexMapper.ProvisionIndex(m_IndexName, m_indexBuilderParams);
            m_indexInitialized = true;
            return branchAlias;
        }

        /// <summary>
        /// Get the file contract batch from the parse store
        /// </summary>
        /// <param name="parseStorePath">Location for reading the parsed content</param>
        /// <param name="metaDataStoresPath">MetaDataStore data location</param>
        /// <param name="skip">Number of files to skip from parse store</param>
        /// <param name="take">Number of files to pick in this batch from parse store. Set it to -1 to create batch based on size</param>
        /// <param name="size">Size of files in this batch. Set it to -1 to prepare batch based on file count</param>
        public IEnumerable<IFileContract> GetFileContractBatch(IObjectStore parseStore, IMetaDataStore metaDataStore, int skip, int take, long size = -1)
        {
            if (null == parseStore)
            {
                throw new ArgumentNullException("GetFileContractBatch", "parsestore");
            }

            if (null == metaDataStore)
            {
                throw new ArgumentNullException("GetFileContractBatch", "metaDataStore");
            }

            return m_fileContracter.GetFileContractBatch(parseStore, metaDataStore, skip, take, size);
        }

        /// <summary>
        /// Once index is built this function create index mapper so that query pipeline can use
        /// this index mapper and query.
        /// </summary>
        public void FinalizeIndex()
        {
            if (m_indexInitialized)
            {
                m_indexMapper.FinalizeIndex();
                m_indexInitialized = false;
                return;
            }
            // TODO: throw an exception
        }
    }
}
