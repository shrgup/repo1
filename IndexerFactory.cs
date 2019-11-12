//-----------------------------------------------------------------------
// <copyright file="IndexerFactory.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Indexer
{
    using System;
    using System.Globalization;
    using System.Linq;

    using Common;
    using Platforms.SearchEngine.Definitions;

    public static class IndexerFactory
    {
        //TODO: Code cleanup for removing Default parameters
        public static IIndexBuilder CreateIndexBuilder(IndexBuilderParams indexParams, ISearchPlatform searchPlatform = null)
        {

            // Check for null parameters            
            if (null == indexParams || string.IsNullOrWhiteSpace(indexParams.Account) ||
                    string.IsNullOrWhiteSpace(indexParams.Collection) ||
                    string.IsNullOrWhiteSpace(indexParams.Project) ||
                    string.IsNullOrWhiteSpace(indexParams.Repo) ||
                    string.IsNullOrWhiteSpace(indexParams.Branch))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "IndexerFactory called with Invalid arguments for Index builder"));
            }

            if (0 >= indexParams.Size)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "IndexerFactory called with Invalid repo size for Index builder"), "Size");
            }

            if (!Enum.IsDefined(typeof(IndexMappingType), indexParams.IndexMapper))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Index Map {0} is not supported", indexParams.IndexMapper.ToString()));
            }

            if (!Enum.IsDefined(typeof(FileContract), indexParams.FileContract))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "ES document mapping {0} is not supported", indexParams.FileContract.ToString()));
            }

            if ((indexParams.IndexMapper == IndexMappingType.Aliasing) &&
                (indexParams.FileContract == FileContract.SourceNoDedupe))
            {
                return new IndexBuilder(
                    new SourceNoDedupeFileContracter(),
                    new AliasProvisioner(searchPlatform ?? GetSearchPlatform()),
                    indexParams
                    );
            }

            throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "IndexMapper: {0} FileContract: {1}",
                                                            indexParams.IndexMapper.ToString(),
                                                            indexParams.FileContract.ToString()));
        }

        public static IQueryIndexMapper CreateIndexMapper(IndexMapperType indexMap)
        {
            if (indexMap != IndexMapperType.Aliasing)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Index Map Type {0} is not supported", indexMap.ToString()));
            }
            return new AliasProvisioner();
        }

        //TODO: Code cleanup for default parameters
        public static IIndexProvisioner CreateIndexProvisioner(IndexProvisionParams provision, ISearchPlatform searchPlatform = null)
        {
            if (null == provision)
            {
                throw new ArgumentNullException("provison is NULL", "provision");
            }

            if (provision.ProvisionType != IndexProvisionType.SingleIndexPerAccount)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "Provision Type {0} is not supported", provision.ProvisionType.ToString()));
            }

            return new SingleIndexPerAccountProvisioner(searchPlatform ?? GetSearchPlatform());
        }

        #region Private method for getting platform
        private static ISearchPlatform GetSearchPlatform()
        {
            return SearchPlatformFactory.Create(ConfigReader.ReadConfig(ConfigReader.ConfigKeys.SearchPlatformConnectionString));
        }
        #endregion
    }
}
