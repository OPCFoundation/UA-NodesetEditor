using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;

using CESMII.ProfileDesigner.Common.Enums;
using CESMII.ProfileDesigner.DAL.Models;
using CESMII.ProfileDesigner.Data.Entities;

namespace CESMII.ProfileDesigner.DAL.Utils
{
    /// <summary>
    /// Mapping helper class to allow multiple areas of the code to perform common mapping routines
    /// and generate complex inter-relational lists for profiles
    /// </summary>
    public class ProfileMapperUtil
    {
        private readonly IDal<ProfileTypeDefinition, ProfileTypeDefinitionModel> _dal;
        private readonly IDal<LookupItem, LookupItemModel> _dalLookup;
        private readonly IDal<LookupDataType, LookupDataTypeModel> _dalDataType;
        private readonly IStoredProcedureDal<ProfileTypeDefinitionSimpleModel> _dalRelated;
        private readonly Common.ConfigUtil _config;
        
        private readonly static List<int?> _excludedProfileTypes = new() { (int)ProfileItemTypeEnum.Object, (int)ProfileItemTypeEnum.Method };
        public static List<int?> ExcludedProfileTypes { 
            get { return _excludedProfileTypes; }
        }

        public const string strOpcHasComponentReferenceNodeId = "nsu=http://opcfoundation.org/UA/;i=47";
        private const string strOpcFolderTypeNodeId = "http://opcfoundation.org/UA/;i=61";
        private const string strOpcOrganizesReferenceNodeId = "nsu=http://opcfoundation.org/UA/;i=35";

        public ProfileMapperUtil(IDal<ProfileTypeDefinition, ProfileTypeDefinitionModel> dal,
            IDal<LookupItem, LookupItemModel> dalLookup,
            IDal<LookupDataType, LookupDataTypeModel> dalDataType,
            IStoredProcedureDal<ProfileTypeDefinitionSimpleModel> dalRelated,
            Common.ConfigUtil config
            )
        {
            _dal = dal;
            _dalLookup = dalLookup;
            _dalDataType = dalDataType;
            _dalRelated = dalRelated;
            _config = config;
        }


        #region Getting, Assembling, Generating Related Profile Data (for explorer, breadcrumb trail, extended attributes)
        /// <summary>
        /// An ordered representation of the profile's lineage. This is a flat collection
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        public List<ProfileTypeDefinitionSimpleModel> GenerateAncestoryLineage(ProfileTypeDefinitionModel profile, UserToken userToken)
        {
            //navigate up the inheritance tree until the root. 
            var result = new List<ProfileTypeDefinitionSimpleModel>();
            int counter = 0;
            var ancestor = profile;
            while (ancestor != null)
            {
                result.Add(MapToModelProfileSimple(ancestor, counter));
                // For Objects we use the instance hierarchy instead of the type of the object
                var parentId = (ancestor.TypeId == (int) ProfileItemTypeEnum.Object && ancestor.InstanceParent != null ? ancestor.InstanceParent?.ID : ancestor.Parent?.ID);
                ancestor = parentId == null ? null : _dal.GetById(parentId.Value, userToken);
                counter--;
            }
            //sort the result grandaprent / parent / profile
            return result.OrderBy(p => p.Level).ThenBy(p => p.Name).ToList();
        }

        /// <summary>
        /// A nested representation of the profile's place relative to its ancestors and siblings
        /// This is a recursive represenation for ancestors and siblings. 
        /// </summary>
        /// <param name="typeDef"></param>
        /// <param name="includeSiblings"></param>
        /// <returns></returns>
        public List<ProfileTypeDefinitionAncestoryModel> GenerateAncestoryTree(ProfileTypeDefinitionModel typeDef, UserToken userToken, bool includeSiblings = false)
        {
            //navigate up the inheritance tree until the root. this is sorted properly. Should be at least one.
            //note this includes the item itself in the lineage.
            var lineage = this.GetAncestors(typeDef.ID.Value ,userToken);

            //convert result set to ProfileTypeDefinitionAncestoryModel which has children collection. 
            //convert result into a hierarchical representation - from root to typeDef item
            ProfileTypeDefinitionAncestoryModel root = null;
            ProfileTypeDefinitionAncestoryModel curItem = null;
            ProfileTypeDefinitionAncestoryModel finalParent = null;
            foreach (var t in lineage.Data)
            {
                var pConverted = new ProfileTypeDefinitionAncestoryModel()
                { ID = t.ID, Author = t.Author, Description = t.Description, Level = t.Level, Name = t.Name, Type = t.Type
                    , /*Namespace = p.Namespace,*/ IsAbstract = t.IsAbstract, OpcNodeId = t.OpcNodeId
                    , Profile = new ProfileModel() { ID = t.Profile.ID, Namespace = t.Profile.Namespace, Version = t.Profile.Version }
                };
                //1st iteration
                if (root == null)
                {
                    root = pConverted;
                    root.Children = new List<ProfileTypeDefinitionAncestoryModel>();
                    finalParent = root;
                }
                else //all other iterations
                {
                    curItem.Children.Add(pConverted);
                    finalParent = curItem;
                }
                curItem = pConverted;
                curItem.Children = new List<ProfileTypeDefinitionAncestoryModel>();
            }

            var result = new List<ProfileTypeDefinitionAncestoryModel>
            {
                root
            };

            //build out a list and siblings will go on same level as the profile
            //find siblings - profiles with same parent. 
            //Append to cur item if there is a multi-gen scenario. append as sibling if profile is the root 
            if (includeSiblings)
            {
                var siblings = _dal.Where(p => !ProfileMapperUtil.ExcludedProfileTypes.Contains(p.ProfileTypeId) /*p.ProfileTypeId != (int)ProfileItemTypeEnum.Object*/ && typeDef.Parent != null &&
                                            p.ParentId.Equals(typeDef.Parent.ID) &&
                                            p.ProfileTypeId.Equals(typeDef.TypeId) &&  //only get siblings of same type
                                            p.ID != typeDef.ID, userToken, null, null, false).Data
                    .Select(s => MapToModelProfileAncestory(s, 1));

                if (lineage.Count == 1) result = result.Concat(siblings).ToList();
                else if (finalParent != null)
                {
                    //append siblings to final parent, sort by name
                    finalParent.Children = finalParent.Children.Concat(siblings)
                        .OrderBy(x => x.Name)
                        .ThenBy(x => x.Profile.Namespace)
                        .ToList();
                }
            }
            return result;
        }

        /// <summary>
        /// Get the list of profiles that depend on this profile
        /// </summary>
        /// <param name="typeDef"></param>
        /// <returns></returns>
        public List<ProfileTypeDefinitionSimpleModel> GenerateDependencies(ProfileTypeDefinitionModel typeDef, UserToken userToken)
        {
            //find all descendants that may be related...go n levels deep
            //var result = new List<ProfileTypeDefinitionSimpleModel>();
            //BuildDescendantsTree(ref result, typeDef.ID, 1, userToken);
            var result = GetDependencies(typeDef.ID.Value, userToken);

            return result.Data;
        }

        /// <summary>
        /// Build a list of ancestors for this type def
        /// </summary>
        private DALResult<ProfileTypeDefinitionSimpleModel> GetAncestors(int id, UserToken userToken)
        {
            var fnName = "public.fn_profile_type_definition_get_ancestors";
            var orderBys = new List<OrderBySimple>() {
                new OrderBySimple() { FieldName = "level" },
                new OrderBySimple() { FieldName = "name" }
            };
            //TBD - pass in paging to this.
            return _dalRelated.GetItemsPaged(fnName, null, null, false, orderBys, id, userToken.UserId);
        }

        /// <summary>
        /// Build a list of descendants that will be used in the dependencies list. 
        /// </summary>
        /// <param name="descendants"></param>
        /// <param name="parentId"></param>
        /// <param name="level"></param>
        private DALResult<ProfileTypeDefinitionSimpleModel> GetDescendants(int id, UserToken userToken
            , bool limitByType, bool excludeIsAbstract, List<OrderBySimple> orderBys = null)
        {
            var fnName = "public.fn_profile_type_definition_get_descendants";
            if (orderBys == null)
            {
                orderBys = new List<OrderBySimple>() {
                    new OrderBySimple() { FieldName = "level" },
                    new OrderBySimple() { FieldName = "profile_title" } ,
                    new OrderBySimple() { FieldName = "profile_namespace" } ,
                    new OrderBySimple() { FieldName = "profile_version" } ,
                    new OrderBySimple() { FieldName = "profile_publish_date" } ,
                    new OrderBySimple() { FieldName = "name" }
                };
            }
            //TBD - pass in paging to this.
            return _dalRelated.GetItemsPaged(fnName, null, null, false, orderBys, id, userToken.UserId, limitByType, excludeIsAbstract);
        }

        /// <summary>
        /// Build a list of dependencies (descendants and peer dependencies)
        /// </summary>
        /// <param name="descendants"></param>
        /// <param name="parentId"></param>
        /// <param name="level"></param>
        private DALResult<ProfileTypeDefinitionSimpleModel> GetDependencies(int id, UserToken userToken)
        {
            var fnName = "public.fn_profile_type_definition_get_dependencies";
            var orderBys = new List<OrderBySimple>() {
                new OrderBySimple() { FieldName = "name" },
                new OrderBySimple() { FieldName = "profile_title" } ,
                new OrderBySimple() { FieldName = "profile_namespace" } ,
                new OrderBySimple() { FieldName = "profile_version" } ,
                new OrderBySimple() { FieldName = "profile_publish_date" } ,
                new OrderBySimple() { FieldName = "level" }
            };
            //TBD - pass in paging to this.
            return _dalRelated.GetItemsPaged(fnName, null, null, false, orderBys, id, userToken.UserId, false, false);
        }

        /// <summary>
        /// TBD - combine this with the lineage call so we reduce churn on DB. 
        /// Get a list of extended attributes for this profile. Extended attributes are attributes that belong to the ancestors of this
        /// profile. These are read only in the front end but used to avoid duplication and to show the complete make up of this profile. 
        /// </summary>
        /// <returns></returns>
        public List<ProfileAttributeModel> GetExtendedAttributes(ProfileTypeDefinitionModel profile, UserToken userToken)
        {
            //navigate up the inheritance tree until the root. 
            var result = new List<ProfileAttributeModel>();
            var ancestor = profile;
            while (ancestor != null)
            {
                ancestor = ancestor.Parent?.ID == null ? null : _dal.GetById(ancestor.Parent.ID.Value, userToken);
                //note we don't get the attribs from the current profile. They are not "extended". 
                if (ancestor?.Attributes != null)
                {
                    result = result.Concat(ancestor.Attributes).ToList();
                }
                //FIX. add compositions to extended attributes
                if (ancestor?.Compositions != null)
                {
                    result = result.Concat(MapCompositionsToProfileAttributeModels(ancestor)).ToList();
                }
            }

            //A profile can implement interface(s). This is indicated by a collection hanging off the profile object.
            //Get the attributes associated with these interfaces. These are essentially "virtual" (read-only) attributes
            //that will show in the list of attributes along side the other attributes. 
            result = result.Concat(MapInterfaceAttributesToProfileAttributeModels(profile, userToken)).ToList();

            //trim down attributes of type def attributes and ancestors where certain things match up. If the ancestor attribute
            //has same name, type, browse name, we only show one.  
            //for now, this arbitrarily chooses the extended attribute to keep. revisit this if we need
            //to only keep a specific extended attribute (ie only the immediate parent's attrib)
            result = result.GroupBy(x => new { x.Name, x.DataTypeId, x.AttributeType.ID, x.BrowseName })
                                  .Select(d => d.First())
                                  .ToList();

            //sort the result by attribute name
            return result.OrderBy(a => a.Name).ToList();
        }

        public List<int> GetPopularItems(UserToken userToken)
        {
            //build list of where clauses
            List<Expression<Func<ProfileTypeDefinition, bool>>> predicate = new();

            //Part 0 - Always exclude some types that are behind the scenes type
            predicate.Add(x => !ProfileMapperUtil.ExcludedProfileTypes.Contains(x.ProfileTypeId));
            predicate.Add(x => x.Favorite != null || x.Analytics != null);

            //build list of order bys - is favorites true first, then the manual ranking, then extend count, then page visit count
            var obes = new List<OrderByExpression<ProfileTypeDefinition>>
            {
                new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Favorite == null || !x.Favorite.IsFavorite ? 0 : 1, IsDescending = true },
                new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Analytics == null ? 0 : x.Analytics.ManualRank, IsDescending = true },
                new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Analytics == null ? 0 : x.Analytics.ExtendCount, IsDescending = true },
                new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Analytics == null ? 0 : x.Analytics.PageVisitCount, IsDescending = true }
            };

            return _dal.Where(predicate, userToken, null, 30, false, false, obes.ToArray()).Data
                .OrderByDescending(x => x.PopularityIndex).Select(x => x.ID.Value).Take(30).ToList();
        }

        public List<OrderByExpression<ProfileTypeDefinition>> BuildSearchOrderByExpressions(int userId, SearchCriteriaSortByEnum val = SearchCriteriaSortByEnum.Name)
        {
            //build list of order bys based on user selection of an enum, default is sort by name. 
            var result = new List<OrderByExpression<ProfileTypeDefinition>>();
            switch (val)
            {
                case SearchCriteriaSortByEnum.Author:
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => string.IsNullOrEmpty(x.Profile.CloudLibraryId) && 
                                                                                    x.AuthorId.HasValue && x.AuthorId.Equals(userId) ? 1 : 0, IsDescending = true });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Name });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Profile.Namespace });
                    break;
                case SearchCriteriaSortByEnum.Popular:
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Favorite == null || !x.Favorite.IsFavorite ? 0 : 1, IsDescending = true });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Analytics == null ? 0 : x.Analytics.ExtendCount, IsDescending = true });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Analytics == null ? 0 : x.Analytics.PageVisitCount, IsDescending = true });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Name });
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Profile.Namespace });
                    break;
                case SearchCriteriaSortByEnum.Name:
                default:
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Name});
                    result.Add(new OrderByExpression<ProfileTypeDefinition>() { Expression = x => x.Profile.Namespace });
                    break;
            }
            
            return result;
        }

        #endregion

        #region Build lookup tables for given profile
        public List<ProfileTypeDefinitionSimpleModel> BuildCompositionLookup(UserToken userToken)
        {
            //CHANGE: 
            //A composition can depend on a type def and that type def can depend on that composition - either directly or indirectly
            //Customer type can have list of orders type. Order can have a customer type. 
            //Recursive - Parent-child are types pointing to themselves so that should be permitted.  
            //compositions can only derive from BaseObjectType - get BaseObjectType profile's dependencies and trim down the
            //list of the compositions if any of these are in the final dependencies list
            var compRoot = _dal.GetAllByFunc(
                x => x.Name.ToLower().Equals(_config.ProfilesSettings.ReservedProfileNames.CompositionRootProfileName.ToLower()),
                userToken, false);
            var orderBys = new List<OrderBySimple>() {
                new OrderBySimple() { FieldName = "profile_namespace" } ,
                new OrderBySimple() { FieldName = "profile_title" } ,
                new OrderBySimple() { FieldName = "profile_version" } ,
                new OrderBySimple() { FieldName = "profile_publish_date" } ,
                new OrderBySimple() { FieldName = "name" }
            };
            List<ProfileTypeDefinitionSimpleModel> lResult = new();
            foreach (var comp in compRoot)
            {
                var abatch = this.GetDescendants(comp.ID.Value, userToken, true, true, orderBys).Data;
                lResult.AddRange(abatch);
            }

            int iItem = 1;
            int count = lResult.Count;
            foreach (ProfileTypeDefinitionSimpleModel xx in lResult)
            {
                System.Diagnostics.Debug.WriteLine($"BuildCompositionLookup: {iItem}/{count}:\tName:{xx.Name}\tModelUri:{xx.Profile.Namespace}");
                iItem++;
            }
            return lResult;
        }

        public List<ProfileTypeDefinitionSimpleModel> BuildVariableTypeLookup(UserToken userToken)
        {
            //CHANGE: 
            //A composition can depend on a type def and that type def can depend on that composition - either directly or indirectly
            //Customer type can have list of orders type. Order can have a customer type. 
            //Recursive - Parent-child are types pointing to themselves so that should be permitted.  
            //compositions can only derive from BaseObjectType - get BaseObjectType profile's dependencies and trim down the
            //list of the compositions if any of these are in the final dependencies list
            var compRoot = _dal.GetByFunc(
                x => x.Name.ToLower().Equals(_config.ProfilesSettings.ReservedProfileNames.VariableTypeRootProfileName.ToLower()),
                userToken, false);
            var orderBys = new List<OrderBySimple>() {
                new OrderBySimple() { FieldName = "profile_namespace" } ,
                new OrderBySimple() { FieldName = "profile_title" } ,
                new OrderBySimple() { FieldName = "profile_version" } ,
                new OrderBySimple() { FieldName = "profile_publish_date" } ,
                new OrderBySimple() { FieldName = "name" }
            };
            var result = this.GetDescendants(compRoot.ID.Value, userToken, true, true, orderBys).Data;
            return result;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// TBD - handle scenario where user implemented same interface in ancestor profile. 
        /// Prevent this from happening by not allowing this as an interface selection.
        ///</remarks>
        /// <param name="typeDef"></param>
        /// <returns></returns>
        public List<ProfileTypeDefinitionModel> BuildInterfaceLookup(ProfileTypeDefinitionModel typeDef, UserToken userToken)
        {
            //remove items of a different type, remove self
            //remove interfaces already used by profile. 
            //Note: interfaces also must only derive from BaseInterfaceType - by selecting only interface profile types, we satisfy this.
            var items = typeDef == null ?
                _dal.Where(p => p.ProfileType.Name.ToLower().Equals("interface"), userToken, null, null, false) : 
                _dal.Where(p => p.ProfileType.Name.ToLower().Equals("interface") &&
                                    !p.ID.Equals(typeDef.ID) &&
                                    !typeDef.Interfaces.Select(x => x.ID).ToList().Contains(p.ID), userToken, null, null, false);
            var result = new List<ProfileTypeDefinitionModel>();
            int counter = 0;
            foreach (var p in items.Data)
            {
                //pull extended attributes from ancestory AND interface attributes
                p.ExtendedProfileAttributes = this.GetExtendedAttributes(p, userToken);
                //merge profile attributes, compositions, variable types
                p.ProfileAttributes = this.MergeProfileAttributes(p);

                //add some special indicators these attrs are interface attr
                foreach (var a in p.ProfileAttributes)
                {
                    a.Interface = new ProfileTypeDefinitionRelatedModel() { ID = p.ID, Name = p.Name, BrowseName = p.BrowseName, OpcNodeId = p.OpcNodeId };
                    a.InterfaceGroupId = counter;
                }

                //add some special indicators the ancestor attrs are interface attr associated with this interface
                foreach (var a in p.ExtendedProfileAttributes)
                {
                    a.Interface = new ProfileTypeDefinitionRelatedModel() { ID = p.ID, Name = p.Name, BrowseName = p.BrowseName, OpcNodeId = p.OpcNodeId }; 
                    a.InterfaceGroupId = counter;
                }

                result.Add(p);

                counter++;
            }

            return result
                .OrderBy(x => x.Profile.Title)
                .ThenBy(x => x.Profile.Namespace)
                .ThenBy(x => x.Profile.Version)
                .ThenBy(x => x.Profile.PublishDate).ToList();
        }

        #endregion

        #region Profiles - Mapping MODEL(s) to ViewModel Methods
        protected ProfileTypeDefinitionAncestoryModel MapToModelProfileAncestory(ProfileTypeDefinitionModel item, int level)
        {
            if (item != null)
            {
                return new ProfileTypeDefinitionAncestoryModel
                {
                    ID = item.ID,
                    Name = item.Name,
                    BrowseName = item.BrowseName,
                    Profile = new ProfileModel() { ID = item.Profile.ID, Namespace = item.Profile.Namespace, Version = item.Profile.Version },
                    Description = item.Description,
                    Type = item.Type,
                    //establish ownership
                    Author = item.Profile == null || 
                             item.Profile.ProfileState == ProfileStateEnum.CloudLibApproved ||
                             item.Profile.ProfileState == ProfileStateEnum.Core ||  
                             item.Profile.ProfileState == ProfileStateEnum.CloudLibPublished ||  
                             item.Profile.ProfileState == ProfileStateEnum.Unknown ? 
                             null : item.Author,
                    OpcNodeId = item.OpcNodeId,
                    IsAbstract = item.IsAbstract,
                    Level = level
                };
            }
            else
            {
                return null;
            }
        }

        public List<ProfileTypeDefinitionSimpleModel> MapToModelProfileSimpleList(List<ProfileTypeDefinitionModel> items)
        {
            var result = new List<ProfileTypeDefinitionSimpleModel>();
            foreach (var p in items)
            {
                result.Add(MapToModelProfileSimple(p));
            }
            return result.OrderBy(a => a.Name).ToList(); 
        }

        public static ProfileTypeDefinitionSimpleModel MapToModelProfileSimple(ProfileTypeDefinitionModel item, int level = 0)
        {
            if (item != null)
            {
                return new ProfileTypeDefinitionSimpleModel
                {
                    ID = item.ID,
                    Name = item.Name,
                    BrowseName = item.BrowseName,
                    /*Namespace = item.Profile.Namespace,*/
                    Profile = item.Profile,
                    ProfileTypeDefinition = item,
                    Description = item.Description,
                    SymbolicName = item.SymbolicName,                     
                    Type = item.Type ?? new LookupItemModel { ID = item.TypeId, LookupType = LookupTypeEnum.ProfileType },
                    Author = item.Author,
                    OpcNodeId = item.OpcNodeId,
                    IsAbstract = item.IsAbstract,
                    Level = level,
                    VariableDataTypeId = item.VariableDataType?.ID,
                };
            }
            else
            {
                return null;
            }

        }

        public ProfileTypeDefinitionSimpleWithAttrsModel MapToModelProfileSimpleWithAttrs(ProfileTypeDefinitionModel item, int level = 0)
        {
            if (item != null)
            {
                return new ProfileTypeDefinitionSimpleWithAttrsModel
                {
                    ID = item.ID,
                    Name = item.Name,
                    BrowseName = item.BrowseName,
                    SymbolicName = item.SymbolicName,
                    /*Namespace = item.Profile.Namespace,*/
                    Profile = new ProfileModel() { ID = item.Profile.ID, Namespace = item.Profile.Namespace, Version = item.Profile.Version },
                    Description = item.Description,
                    Type = item.Type,
                    Author = item.Author,
                    OpcNodeId = item.OpcNodeId,
                    IsAbstract = item.IsAbstract,
                    Level = level,
                    ProfileAttributes = item.Attributes
                };
            }
            else
            {
                return null;
            }

        }
        #endregion

        #region Attributes - Mapping MODEL(s) to ViewModel Methods
        /// <summary>
        /// Take the profile item attributes, compositions, variable types and merge into a single 
        /// collection that the front end can use. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public List<ProfileAttributeModel> MergeProfileAttributes(ProfileTypeDefinitionModel item)
        {
            var result = item.Attributes ?? new List<ProfileAttributeModel>();
            result = result.Concat(MapCompositionsToProfileAttributeModels(item)).ToList();

            //trim down where comparable item is in the parent (match on name, data type id, attribute type id, browse name)
            //for now, we trim out the profile attribute and leave extended so we get the read-only ui
            if (item.ExtendedProfileAttributes?.Count > 0)
            {
                result = result.Where(x => !item.ExtendedProfileAttributes.Any(y =>
                        y.Name == x.Name &&
                        y.DataTypeId == x.DataTypeId &&
                        y.AttributeType.ID == x.AttributeType.ID &&
                        y.BrowseName == x.BrowseName)).ToList();
            }

            //sort by enum value if present, then by name
            return result
                .OrderByDescending(a => a.EnumValue.HasValue)
                .ThenBy(a => a.EnumValue)
                .ThenBy(a => a.Name)
                .ToList();
        }

        /// <summary>
        /// For a given profile, map the composition attributes to a unified model
        /// structure (ProfileAttributeModel) that can be used downstream when merging 
        /// all sttributes into a single collection. This will add some data to distinguish
        /// these as composition type attributes. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        protected List<ProfileAttributeModel> MapCompositionsToProfileAttributeModels(ProfileTypeDefinitionModel item)
        {
            var result = new List<ProfileAttributeModel>();
            if (item == null || item.Compositions == null) return result;

            //get lookup data type for composition
            var dataTypeComp = _dalDataType.Where(x => x.Code.ToLower().Equals("composition"), new UserToken(), null, null).Data.FirstOrDefault();
            //get lookup data type for attr type composition
            var attrTypeComp = _dalLookup.Where(x => x.Code.ToLower().Equals("composition"), new UserToken(), null, null).Data.FirstOrDefault();

            foreach (var comp in item.Compositions)
            {
                result.Add(new ProfileAttributeModel
                {
                    Composition = comp,
                    CompositionId = comp.RelatedProfileTypeDefinitionId,
                    ID = comp.ID,
                    Name = comp.Name,
                    OpcNodeId = comp.OpcNodeId,
                    BrowseName = comp.BrowseName,
                    SymbolicName = comp.SymbolicName,
                    Description = comp.Description,
                    IsRequired = comp.IsRequired,
                    ModelingRule = comp.ModelingRule,
                    TypeDefinitionId = item.ID,
                    //TypeDefinition = item, // Results in cycle during serialization
                    // No VariableTypeDefinition for compositions
                    DataTypeId = dataTypeComp.ID,
                    DataType = dataTypeComp, 
                    AttributeType = attrTypeComp
                });
            }
            return result;
        }

        /// <summary>
        /// For each interface associated with the profile, append the interface attribute into the 
        /// profile attributes list. These would be read only and show how the profile is virtually implementing
        /// the attributes for each interface. 
        /// </summary>
        /// <remarks>
        /// TBD - need to get the ancestor attributes of each interface as well. 
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        protected List<ProfileAttributeModel> MapInterfaceAttributesToProfileAttributeModels(ProfileTypeDefinitionModel item, UserToken userToken)
        {
            var result = new List<ProfileAttributeModel>();
            if (item == null || item.Interfaces == null) return result;

            var counter = 0;
            foreach (var p in item.Interfaces)
            {
                var attrs = this.MergeProfileAttributes(p);

                //get the ancestor attribs which are also now a part of this interface implementation
                attrs = attrs.Concat(this.GetExtendedAttributes(p, userToken)).ToList();

                //add some special indicators these attrs are interface attr all associated with this interface
                //even acncestor attribs should be associated with this interface. 
                foreach (var a in attrs)
                {
                    a.Interface = new ProfileTypeDefinitionRelatedModel() { ID = p.ID, Name = p.Name, BrowseName = p.BrowseName, OpcNodeId = p.OpcNodeId }; 
                    a.InterfaceGroupId = counter;
                }

                result = result.Concat(attrs).ToList();
                counter++;
            }
            return result;
        }


        #endregion

        #region Mapping ViewModel(s) to MODEL(s) to ENTITY(s)
        /// <summary>
        /// Take a list of attributes returned by front end and find the ones which compositions. Split those out 
        /// into a collection that the dal and DB is expecting.
        /// </summary>
        /// <param name="comps"></param>
        /// <returns></returns>
        public List<ProfileTypeDefinitionRelatedModel> MapProfileAttributeToCompositionModels(List<ProfileAttributeModel> comps)
        {
            var result = new List<ProfileTypeDefinitionRelatedModel>();
            if (comps == null || comps.Count == 0) return result;

            foreach (var a in comps)
            {
                result.Add(new ProfileTypeDefinitionRelatedModel
                {
                    ID = a.ID,
                    RelatedProfileTypeDefinitionId = a.CompositionId.Value,
                    RelatedProfileTypeDefinition = new ProfileTypeDefinitionModel() { ID = a.CompositionId.Value,  
                        Name = a.Composition.Name, Description = a.Composition.Description 
                    },
                    IntermediateObject = a.Composition.IntermediateObject,
                    IntermediateObjectId = a.Composition.IntermediateObjectId,
                    IntermediateObjectName = a.Composition.IntermediateObjectName,
                    Name = a.Name,
                    OpcNodeId = a.OpcNodeId,
                    SymbolicName = a.SymbolicName,
                    Description = a.Description,
                    BrowseName = a.BrowseName,
                    IsRequired = a.IsRequired,
                    ModelingRule = a.ModelingRule,
                    Author = a.Composition.Author
                });
            }
            return result;
        }

        internal static bool IsHasComponentReference(string relatedReferenceId)
        {
            return string.IsNullOrEmpty(relatedReferenceId) || relatedReferenceId == strOpcHasComponentReferenceNodeId;
        }
        internal static string GetReferenceTypeForObjectType(string nodeId)
        {
            if (nodeId?.EndsWith(strOpcFolderTypeNodeId) == true)
            {
                // Organizes reference is used for references from objects of type FolderType
                return strOpcOrganizesReferenceNodeId;
            }
            return null;
        }

        #endregion

        #region Mapping MODEL(s) to ENTITY(s) Methods
        #endregion


    }
}
