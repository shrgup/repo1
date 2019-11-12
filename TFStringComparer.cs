// ************************************************************************************************
// Microsoft Team Foundation
//
// Microsoft Confidential
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// File:        TFStringComparer.cs
// Area:        Team Foundation
// Classes:     TFStringComparer
// Contents:    The Team Foundation string comparison class provides inner classes
//              that are used to provide semantic-specific Equals and Compare methods
//              and a semantic-specific StringComparer instance.  New semantics should
//              be added on an as-needed basis.
// ************************************************************************************************
using System;
using System.Diagnostics;
using Microsoft.TeamFoundation.Common;
using Microsoft.TeamFoundation.Common.Internal;
using Microsoft.VisualStudio.Services.Common;

namespace Microsoft.TeamFoundation
{

// NOTE: Since the recommendations are for Ordinal and OrdinalIgnoreCase, no need to explain those, but
//       please explain any instances using non-Ordinal comparisons (CurrentCulture, InvariantCulture)
//       so that developers following you can understand the choices and verify they are correct.

// NOTE: please try to keep the semantic-named properties in alphabetical order to ease merges

// NOTE: do NOT add xml doc comments - everything in here should be a very thin wrapper around String
//       or StringComparer.  The usage of the methods and properties in this class should be intuitively
//       obvious, so please don't add xml doc comments to this class since it should be wholly internal
//       by the time we ship.

// NOTE: Current guidelines from the CLR team (Dave Fetterman) is to stick with the same operation for both
//       Compare and Equals for a given semantic inner class.  This has the nice side effect that you don't
//       get different behavior between calling Equals or calling Compare == 0.  This may seem odd given the
//       recommendations about using CurrentCulture for UI operations and Compare being used for sorting
//       items for user display in many cases, but we need to have the type of string data determine the
//       string comparison enum to use instead of the consumer of the comparison operation so that we're
//       consistent in how we treat a given semantic.

// TFStringComparer should act like StringComparer with a few additional methods for usefulness (Contains,
// StartsWith, EndsWith, etc.) so that it can be a "one-stop shop" for string comparisons.
public class TFStringComparer : StringComparer
{
    private StringComparison m_stringComparison;
    private StringComparer m_stringComparer;

    private TFStringComparer(StringComparison stringComparison)
        : base()
    {
        m_stringComparison = stringComparison;
    }

    // pass-through implementations based on our current StringComparison setting
    public override int Compare(string x, string y) { return String.Compare(x, y, m_stringComparison); }
    public override bool Equals(string x, string y) { return String.Equals(x, y, m_stringComparison); }
    public override int GetHashCode(string x) { return MatchingStringComparer.GetHashCode(x); }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "y")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "x")]
    public int Compare(string x, int indexX, string y, int indexY, int length) { return String.Compare(x, indexX, y, indexY, length, m_stringComparison); }
    
    // add new useful methods here
    public bool Contains(string main, string pattern)
    {
        ArgumentUtility.CheckForNull(main, "main");
        ArgumentUtility.CheckForNull(pattern, "pattern");

        return main.IndexOf(pattern, m_stringComparison) >= 0;
    }

    public int IndexOf(string main, string pattern)
    {
        ArgumentUtility.CheckForNull(main, "main");
        ArgumentUtility.CheckForNull(pattern, "pattern");

        return main.IndexOf(pattern, m_stringComparison);
    }

    public bool StartsWith(string main, string pattern)
    {
        ArgumentUtility.CheckForNull(main, "main");
        ArgumentUtility.CheckForNull(pattern, "pattern");

        return main.StartsWith(pattern, m_stringComparison);
    }

    public bool EndsWith(string main, string pattern)
    {
        ArgumentUtility.CheckForNull(main, "main");
        ArgumentUtility.CheckForNull(pattern, "pattern");

        return main.EndsWith(pattern, m_stringComparison);
    }

    private StringComparer MatchingStringComparer
    {
        get
        {
            if (m_stringComparer == null)
            {
                switch (m_stringComparison)
                {
                    case StringComparison.CurrentCulture: 
                        m_stringComparer = StringComparer.CurrentCulture;
                        break;

                    case StringComparison.CurrentCultureIgnoreCase:
                        m_stringComparer = StringComparer.CurrentCultureIgnoreCase;
                        break;

                    case StringComparison.Ordinal:
                        m_stringComparer = StringComparer.Ordinal;
                        break;

                    case StringComparison.OrdinalIgnoreCase:
                        m_stringComparer = StringComparer.OrdinalIgnoreCase;
                        break;

                    default:
                        Debug.Fail("Unknown StringComparison value");
                        m_stringComparer = StringComparer.Ordinal;
                        break;
                }
            }
            return m_stringComparer;
        }
    }

    private static TFStringComparer s_ordinal = new TFStringComparer(StringComparison.Ordinal);
    private static TFStringComparer s_ordinalIgnoreCase = new TFStringComparer(StringComparison.OrdinalIgnoreCase);
    private static TFStringComparer s_currentCulture = new TFStringComparer(StringComparison.CurrentCulture);
    private static TFStringComparer s_currentCultureIgnoreCase = new TFStringComparer(StringComparison.CurrentCultureIgnoreCase);
    private static TFStringComparer s_dataSourceIgnoreProtocol = new DataSourceIgnoreProtocolComparer();

    public static TFStringComparer AnnotationName { get { return s_ordinal; } }
    public static TFStringComparer ArtifactType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ArtifactTool { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AssemblyName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CaseSensitiveFileName { get { return s_ordinal; } }
    public static TFStringComparer ChangeType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ChangeTypeUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer CheckinNoteName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CheckinNoteNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer CommandLineOptionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CommandLineOptionValue { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Comment { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer CommentUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ConflictDescription { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ConflictDescriptionUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ConflictType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ConflictTypeUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ContentType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DomainName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DomainNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer DatabaseCategory { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DatabaseName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DataSource { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DataSourceIgnoreProtocol { get { return s_dataSourceIgnoreProtocol; } }
    public static TFStringComparer EncodingName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer EnvVar { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExceptionSource { get { return s_ordinalIgnoreCase; } }
    // VNEXT: integrate with FileSpec class in HatUtil
    public static TFStringComparer FilePath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer FilePathUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer FileType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Guid { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Hostname { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer HostnameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer HttpRequestMethod { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer InstanceId { get { return s_ordinal; } }
    public static TFStringComparer IdentityDescriptor { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer LabelName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer LabelNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer LinkName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer MailAddress { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ObjectId { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer PermissionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer PolicyType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer PropertyName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer QuotaName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer RegistrationAttributeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ReservedGroupName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServerUrl { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServerUrlUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ServiceInterface { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServicingOperation { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ShelvesetName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ShelvesetNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer SoapExceptionCode { get { return s_ordinal; } }
    public static TFStringComparer SubscriptionFieldName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer SubscriptionTag { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TeamProjectCollectionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TeamProjectCollectionNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer TeamProjectName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TeamProjectNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer TeamNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer TFSName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TFSNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ToolId { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Url { get { return s_ordinal; } }
    public static TFStringComparer UrlPath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer UriScheme { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer UserName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer UserNameUI { get { return s_currentCultureIgnoreCase; } }
    // VNEXT: integrate with VersionControlPath class in HatUtil
    public static TFStringComparer VersionControlPath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemQueryName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer WorkItemQueryText { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemStateName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer WorkItemActionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemTypeName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer WorkspaceName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkspaceNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer XmlAttributeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer XmlNodeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer XmlElement { get { return s_ordinal; } }
    // Use this comparer when determining whether an object property has been changed or should be updated
    public static TFStringComparer WorkItemUpdate { get { return s_ordinal; } }

    //Framework comparers.
    public static TFStringComparer RegistryPath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServiceType { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer AccessMappingMoniker { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CatalogNodePath { get { return s_ordinal; } }
    public static TFStringComparer CatalogServiceReference { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CatalogNodeDependency { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServicingTokenName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer IdentityPropertyName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Collation { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer FeatureAvailabilityName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TagName { get { return s_currentCultureIgnoreCase; } }

    //Framework Hosting comparers.
    public static TFStringComparer HostingAccountPropertyName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer MessageBusName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer MessageBusSubscriptionName { get { return s_ordinalIgnoreCase; } }

    //TeamBuild comparers.
    public static TFStringComparer BuildAgent { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildAgentUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer BuildControllerName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildControllerNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer BuildName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildNumber { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildStep { get { return s_ordinal; } }
    public static TFStringComparer BuildPlatformFlavor { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildTargetName { get { return s_ordinal; } }
    public static TFStringComparer BuildTaskName { get { return s_ordinal; } }
    public static TFStringComparer BuildType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildTypeUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer BuildQuality { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BuildQualityUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer InformationType { get { return s_ordinal; } }
    public static TFStringComparer TestCategoryName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TestCategoryNameUI { get { return s_currentCultureIgnoreCase; } }

    // WorkItemTracking comparers    
    public static TFStringComparer DataType { get { return s_ordinal; } }
    public static TFStringComparer SID { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer StructureType { get { return s_ordinal; } }
    public static TFStringComparer FieldName { get { return s_ordinal; } }
    public static TFStringComparer FieldNameUI { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer FieldType { get { return s_ordinal; } }
    public static TFStringComparer QueryOperator { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssActions { get { return s_ordinal; } }
    public static TFStringComparer UpdateAction { get { return s_ordinal; } }
    public static TFStringComparer EventType { get { return s_ordinal; } }
    public static TFStringComparer EventTypeIgnoreCase { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer RegistrationEntryName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemArtifactType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ServerName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AutoCompleteComboBox   { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer GroupName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer WorkItemCategoryName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemCategoryReferenceName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer LinkComment { get { return s_ordinal; } }
    public static TFStringComparer ControlType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ControlClassName { get { return s_ordinalIgnoreCase; } }

    // ELead Comparers
    public static TFStringComparer ListViewItem { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer NodeSpec { get { return s_ordinal; } }
    public static TFStringComparer CssNodeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer FavoritesNodePath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ProjectCreationContextData { get { return s_ordinal; } }
    public static TFStringComparer TemplateName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WssListElement { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WssTemplate { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WssFilePath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OfficeVersions { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssProjectPropertyName { get { return s_ordinal; } }
    public static TFStringComparer FactName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ParserTag { get { return s_ordinal; } }
    public static TFStringComparer StringFieldConditionEquality { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer StringFieldConditionOrdinal { get { return s_ordinal; } }
    public static TFStringComparer StringUtilityComparison { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BoolAppSetting { get { return s_ordinal; } }
    public static TFStringComparer IdentityData { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ProjectString { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer RegistrationUtilities { get { return s_ordinal; } }
    public static TFStringComparer RegistrationUtilitiesCaseInsensitive { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer IdentityName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer PlugInId { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExtensionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer DomainUrl { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssStructureType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssXmlNodeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssTreeNodeName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer CssXmlNodeInfoUri { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CssTreePathName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer RegistrationEntryType { get { return s_ordinal; } }
    public static TFStringComparer DataGridId { get { return s_currentCulture; } }
    public static TFStringComparer ArtiFactUrl { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AclPermissionEntry { get { return s_currentCulture; } }
    public static TFStringComparer DirectoryEntrySchemaClassName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AccountInfoAccount { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AccountInfoPassword { get { return s_ordinal; } }
    public static TFStringComparer RoleMemberName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BaseHierarchyNodeName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer BaseHierarchyNodePath { get { return s_ordinal; } }
    public static TFStringComparer BaseUIHierarchyNodeName { get { return s_ordinal; } }
    public static TFStringComparer CanonicalName { get { return s_ordinal; } }
    public static TFStringComparer CreateDSArg { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer CatalogItemName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer Verb { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ProgId { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OfficeWorkItemId { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer TfsProtocolUriComponent { get { return s_ordinalIgnoreCase; } }

    // Requirement Management Comparers
    public static TFStringComparer StoryboardStencilReferenceName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer StoryboardLinkPath { get { return s_ordinalIgnoreCase; } }

    public static TFStringComparer AllowedValue { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ProjectUri { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ELeadListObjectName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer HashCode { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemId { get { return s_ordinal; } }
    public static TFStringComparer WorkItemRev { get { return s_ordinal; } }
    public static TFStringComparer WorkItemType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExcelValidationData { get { return s_ordinal; } }
    public static TFStringComparer ExcelValidationDataIgnoreCase { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkbookName { get { return s_ordinal; } }
    public static TFStringComparer WorksheetName { get { return s_ordinal; } }
    public static TFStringComparer ExcelListName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExcelColumnName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExcelWorkSheetName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExcelNumberFormat { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer ExcelBannerText { get { return s_ordinal; } }
    public static TFStringComparer WorkItemFieldReferenceName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemLinkTypeReferenceName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WorkItemFieldFriendlyName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer StoredQueryName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer StoredQueryText { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer AttachmentName { get { return s_ordinal; } }
    public static TFStringComparer LinkData { get { return s_ordinal; } }
    public static TFStringComparer CssSprocErrors { get { return s_ordinalIgnoreCase; } }

    public static TFStringComparer MSProjectDisplayableObjectName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer MSProjectAssignmentName { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer MSProjectFieldName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer MSProjectCellValue { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer CaseInsensitiveArrayList { get { return s_currentCultureIgnoreCase; } }
    public static TFStringComparer ProjMapArgs { get { return s_ordinalIgnoreCase; } }

    // Warehouse object names
    public static TFStringComparer WareHouseFieldName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WareHouseDimensionName { get { return s_ordinalIgnoreCase; } }

    // OLAP object names - should only be used internally by the warehouse framework
    public static TFStringComparer OlapProcessingType { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapAccessUser { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapConnectionString { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapDimensionAnnotation { get { return s_ordinal; } }
    public static TFStringComparer OlapDimensionName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapDimensionAttributeID { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapDimensionID { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer OlapFriendlyNameLookup { get { return s_ordinalIgnoreCase; } }

    // Converters comparer
    public static TFStringComparer VSSServerPath { get { return s_ordinalIgnoreCase; } }
    
    // Item rename in VSS is case sensitive.
    public static TFStringComparer VSSItemName { get { return s_ordinal; } }

    public static TFStringComparer SDServerPath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer XmlAttributeValue { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer WIConverterFieldRefName { get { return s_ordinalIgnoreCase; } }

    // Reporting Services
    public static TFStringComparer ReportItemPath { get { return s_ordinalIgnoreCase; } }

    //SharePoint Integration Comparers
    public static TFStringComparer SharePointAbsolutePath { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer SharePointRedirectionArgument { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer SharePointPropertyValueSearch { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer SharePointPropertyValueDirty { get { return s_ordinal; } }
    public static TFStringComparer SharePointSolutionName { get { return s_ordinalIgnoreCase; } }

    public static TFStringComparer DiagnosticAreaPathName { get { return s_ordinalIgnoreCase; } }

    // Web Access Comparers
    public static TFStringComparer HtmlElementName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer HtmlAttributeName { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer HtmlAttributeValue { get { return s_ordinalIgnoreCase; } }
    public static TFStringComparer BoardColumnName { get { return s_ordinalIgnoreCase; } }

    private class DataSourceIgnoreProtocolComparer : TFStringComparer
    {
        public DataSourceIgnoreProtocolComparer()
            : base(StringComparison.OrdinalIgnoreCase)
        {
        }

        public override int Compare(string x, string y)
        { 
            return base.Compare(RemoveProtocolPrefix(x), RemoveProtocolPrefix(y));
        }

        public override bool Equals(string x, string y)
        {
            return base.Equals(RemoveProtocolPrefix(x), RemoveProtocolPrefix(y));
        }

        private string RemoveProtocolPrefix(string x)
        {
            if (x != null)
            {
                if (x.StartsWith(c_tcpPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    x = x.Substring(c_tcpPrefix.Length);
                }
                else if (x.StartsWith(c_npPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    x = x.Substring(c_npPrefix.Length);
                }
            }

            return x;
        }

        private const string c_tcpPrefix = "tcp:";
        private const string c_npPrefix = "np:";
    }
}
}
