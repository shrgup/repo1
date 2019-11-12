//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.Search.Query.Extensions
{
    using System.Text;

    using WebApi;

    public static class Extensions
    {
        private static string Tabstop = "  "; // 2 spaces

        /// <summary>
        /// Extension method that converts CodeQueryResponse to multi-line string
        /// with specified indentation of its members.
        /// </summary>
        /// <param name="codeQueryResponse"> CodeQueryResponse instance. </param>
        /// <param name="indentLevel"> Indentation level. 
        ///     Indentation spacing is indentLevel * Tabstop.Length </param>
        /// <returns> CodeQueryResponse as string. </returns>
        public static string Text(this CodeQueryResponse codeQueryResponse, int indentLevel = 0)
        {
            if (codeQueryResponse == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            string spacing = GetIndentSpacing(indentLevel);

            sb.AppendLine(spacing, "Search query:")
              .Append(codeQueryResponse.Query.Text(indentLevel + 1));
            sb.AppendLine(spacing, "Code results:")
              .Append(codeQueryResponse.Results.Text(indentLevel + 1));
            sb.AppendLine(spacing, "Filter categories:");

            foreach (var filterCategory in codeQueryResponse.FilterCategories)
            {
                sb.Append(filterCategory.Text(indentLevel + 1));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts SearchQuery to string to multi-line string
        /// with specified indentation of its members.
        /// </summary>
        /// <param name="searchQuery"> SearchQuery instance. </param>
        /// <param name="indentLevel"> Indentation level. 
        ///     Indentation spacing is indentLevel * Tabstop.Length </param>
        /// <returns> SearchQuery as string. </returns>
        public static string Text(this SearchQuery searchQuery, int indentLevel = 0)
        {
            if (searchQuery == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            string spacing = GetIndentSpacing(indentLevel);

            sb.Append(spacing, "SearchText: ")
              .AppendLine(searchQuery.SearchText);

            sb.Append(spacing, "Scope :")
              .AppendLine(searchQuery.Scope);

            sb.Append(spacing, "Skip: ")
              .AppendLine(searchQuery.SkipResults.ToString());

            sb.Append(spacing, "Take: ")
              .AppendLine(searchQuery.TakeResults.ToString());

            sb.Append(spacing, "Filters:")
              .AppendLine();

            foreach (var filter in searchQuery.Filters)
            {
                sb.AppendLine(filter.Text(indentLevel + 1));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts SearchFilter to string.
        /// </summary>
        /// <param name="filter"> SearchFilter instance. </param>
        /// <returns> SearchFilter as string. </returns>
        public static string Text(this SearchFilter filter, int indentLevel = 0)
        {
            if (filter == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            sb.Append(GetIndentSpacing(indentLevel), "Name: ")
              .Append(filter.Name)
              .AppendFormat(", {{ {0} }}", string.Join(", ", filter.Values))
              .AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts FilterCategory to string.
        /// </summary>
        /// <param name="filterCategory"> FilterCategory instance. </param>
        /// <returns> FilterCategory as string. </returns>
        public static string Text(this FilterCategory filterCategory, int indentLevel = 0)
        {
            if (filterCategory == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            string spacing = GetIndentSpacing(indentLevel);

            sb.Append(spacing, "Name: ")
              .AppendLine(filterCategory.Name)
              .Append(spacing, "Filters:")
              .AppendLine();

            foreach (var filter in filterCategory.Filters)
            {
                sb.AppendLine(filter.Text(indentLevel + 1));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts Filter to string.
        /// </summary>
        /// <param name="filter"> Filter instance. </param>
        /// <returns> Filter as string. </returns>
        public static string Text(this Filter filter, int indentLevel = 0)
        {
            if (filter == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            sb.Append(GetIndentSpacing(indentLevel), "Name: ").Append(filter.Name)
              .Append(", #results: ").Append(filter.ResultCount)
              .Append(", IsSelected: ").Append(filter.Selected);

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts CodeResults to string.
        /// </summary>
        /// <param name="codeResults"> CodeResults instance. </param>
        /// <returns> CodeResults as string. </returns>
        public static string Text(this CodeResults codeResults, int indentLevel = 0)
        {
            if (codeResults == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            string spacing = GetIndentSpacing(indentLevel);

            sb.Append(spacing, "Count: ")
              .AppendLine(codeResults.Count.ToString());

            foreach (var codeResult in codeResults.Values)
            {
                sb.AppendFormat(spacing, "{0}: ")
                  .Append(codeResult.Text(indentLevel + 1));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extension method that converts CodeResult to string.
        /// </summary>
        /// <param name="codeResult"> CodeResult instance. </param>
        /// <returns> CodeResult as string. </returns>
        public static string Text(this CodeResult codeResult, int indentLevel = 0)
        {
            if (codeResult == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            string spacing = GetIndentSpacing(indentLevel);

            sb.AppendFormat("{0}\\{1}\\{2}\\{3}\\{4}\\{5}\\{6}\\{7}",
                            spacing,
                            codeResult.Account,
                            codeResult.Collection,
                            codeResult.Project,
                            codeResult.Repository,
                            codeResult.Version,
                            codeResult.Path,
                            codeResult.Filename);
            sb.AppendLine();

            sb.Append(spacing, "# of hits:")
              .AppendLine(codeResult.HitCount.ToString());

            return sb.ToString();
        }

        private static StringBuilder Append(this StringBuilder sb, string spacing, string s)
        {
            return sb.Append(spacing)
                     .Append(s);
        }

        private static StringBuilder AppendLine(this StringBuilder sb, string spacing, string s)
        {
            return sb.Append(spacing)
                     .AppendLine(s);
        }

        private static string GetIndentSpacing(int indentLevel)
        {
            if (indentLevel == 0)
            {
                return string.Empty;
            }

            return new StringBuilder().Append(' ', Tabstop.Length * indentLevel).ToString();
        }
    }
}
