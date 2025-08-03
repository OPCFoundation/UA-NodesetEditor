namespace CESMII.ProfileDesigner.DAL
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using CESMII.ProfileDesigner.DAL.Models;
    using CESMII.ProfileDesigner.Data.Entities;
    using CESMII.ProfileDesigner.Data.Repositories;

    using Newtonsoft.Json;
    using CESMII.ProfileDesigner.Common.Enums;
    using System.Runtime.Serialization;
    using CESMII.ProfileDesigner.DAL.Utils;
    using System.ComponentModel;

    public class ProfileTypeDefinitionDAL : TenantBasePdDAL<ProfileTypeDefinition, ProfileTypeDefinitionModel>
    {
        public ProfileTypeDefinitionDAL(IRepository<ProfileTypeDefinition> repo, IDal<Profile, ProfileModel> profileDAL, IDal<LookupDataType, LookupDataTypeModel> dataTypeDAL, IDal<EngineeringUnit, EngineeringUnitModel> euDAL, /*IDal<LookupItem, LookupItemModel> lookupDAL,*/ IDal<ProfileTypeDefinitionAnalytic, ProfileTypeDefinitionAnalyticModel> analyticsDal, ILogger<ProfileTypeDefinitionDAL> diLogger) : base(repo)
        {
            _profileDAL = profileDAL as ProfileDAL;
            _dataTypeDAL = dataTypeDAL as LookupDataTypeDAL;
            _euDAL = euDAL as EngineeringUnitDAL;
            _analyticsDal = analyticsDal;
            this._diLogger = diLogger;
        }
        private readonly ProfileDAL _profileDAL;
        private readonly LookupDataTypeDAL _dataTypeDAL;
        private readonly EngineeringUnitDAL _euDAL;
        private readonly IDal<ProfileTypeDefinitionAnalytic, ProfileTypeDefinitionAnalyticModel> _analyticsDal;
        private readonly ILogger<ProfileTypeDefinitionDAL> _diLogger;

        /// <summary>
        /// Changes behavior on read for some profile types/compositions.Import/export require these optimization to be turned off for full NodeSet fidelity.
        /// </summary>
        public bool GenerateIntermediateCompositionObjects { get; set; } = true;

        //add this layer so we can instantiate the new entity here.
        public override async Task<int?> AddAsync(ProfileTypeDefinitionModel model, UserToken userToken)
        {
            if (model.Profile == null && model.ProfileId != null)
            {
                model.Profile = _profileDAL.GetById(model.ProfileId.Value, userToken);
            }
            var entity = new ProfileTypeDefinition();
            model.ID = await base.AddAsync(entity, model, userToken);
            return model.ID;
        }

        /// <summary>
        /// Get one
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public override ProfileTypeDefinitionModel GetById(int id, UserToken userToken)
        {
            var entity = base.FindByCondition(userToken, x => x.ID == id)
                .Include(p => p.ProfileType)
                .Include(p => p.Attributes)
                .FirstOrDefault();

            return MapToModel(entity, true);
        }

        /// <summary>
        /// Get item by id - asynchronously
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public override async Task<ProfileTypeDefinitionModel> GetByIdAsync(int id, UserToken userToken)
        {
            var entity = await base.FindByCondition(userToken, x => x.ID == id)
                .Include(p => p.ProfileType)
                .Include(p => p.Attributes)
                .FirstOrDefaultAsync();

            return MapToModel(entity, true);
        }


        public async Task<int> GetCountByProfileIdAsync(int profileId, UserToken userToken)
        {
            var count = await base.FindByCondition(userToken, x => x.ProfileId == profileId)
                .CountAsync();
            return count;
        }


        /// <summary>
        /// Get all 
        /// </summary>
        /// <returns></returns>
        public override DALResult<ProfileTypeDefinitionModel> GetAllPaged(UserToken userToken, int? skip, int? take, bool returnCount = false, bool verbose = false)
        {
            //put the order by and where clause before skip.take so we skip/take on filtered/ordered query 
            var query = base.GetAllEntities(userToken)
                .OrderBy(p => p.Name);
            var count = returnCount ? query.Count() : 0;
            //query returns IincludableQuery. Jump through the following to find right combo of skip and take
            //Goal is to have the query execute and not do in memory skip/take
            IQueryable<ProfileTypeDefinition> data;
            if (skip.HasValue && take.HasValue) data = query.Skip(skip.Value).Take(take.Value);
            else if (skip.HasValue) data = query.Skip(skip.Value);
            else if (take.HasValue) data = query.Take(take.Value);
            else data = query;

            var result = new DALResult<ProfileTypeDefinitionModel>
            {
                Count = count,
                Data = MapToModels(data.ToList(), verbose),
                SummaryData = null
            };
            return result;
        }

        /// <summary>
        /// Get all with a flexible where clause.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public override DALResult<ProfileTypeDefinitionModel> Where(Expression<Func<ProfileTypeDefinition, bool>> predicate, UserToken user, int? skip = null, int? take = null,
            bool returnCount = false, bool verbose = false)
        {
            return base.Where(predicate, user, skip, take, returnCount, verbose, q => q
                .OrderBy(p => p.Name)
                .Include(p => p.ProfileType)
                .Include(p => p.Attributes)
            );
        }

        /// <summary>
        /// Get all with a list of where clauses.
        /// Calling code can build up any sequence of complex where clauses that will be put together here. 
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public override DALResult<ProfileTypeDefinitionModel> Where(List<Expression<Func<ProfileTypeDefinition, bool>>> predicates,
            UserToken user, int? skip = null, int? take = null, bool returnCount = false, bool verbose = false,
            params OrderByExpression<ProfileTypeDefinition>[] orderByExpressions)
        {
            if (predicates == null) predicates = new List<Expression<Func<ProfileTypeDefinition, bool>>>();

            //build up a query and append n predicates
            var query = _repo.GetAll().AsQueryable<ProfileTypeDefinition>();
            foreach (var p in predicates)
            {
                query = query.Where(p).AsQueryable<ProfileTypeDefinition>();
            }
            //FIX - add filter out by user
            query = query.Where(x => x.OwnerId == null || x.OwnerId == user.UserId).AsQueryable<ProfileTypeDefinition>();

            //append order bys
            if (orderByExpressions == null)
            {
                query = query.OrderBy(p => p.Name);
            }
            else
            {
                //append order by
                ApplyOrderByExpressions(ref query, orderByExpressions);
            }

            //add include
            query = query
                .Include(p => p.ProfileType);

            var count = returnCount ? query.Count() : 0;

            //query returns IincludableQuery. Jump through the following to find right combo of skip and take
            //Goal is to have the query execute and not do in memory skip/take
            IQueryable<ProfileTypeDefinition> data;
            if (skip.HasValue && take.HasValue) data = query.Skip(skip.Value).Take(take.Value);
            else if (skip.HasValue) data = query.Skip(skip.Value);
            else if (take.HasValue) data = query.Take(take.Value);
            else data = query;

            //put together the result
            var result = new DALResult<ProfileTypeDefinitionModel>
            {
                Count = count,
                Data = MapToModels(data.ToList(), verbose),
                SummaryData = null
            };
            return result;
        }

        /// <summary>
        /// Update profile and associated data
        /// </summary>
        /// <param name="model"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public override async Task<int?> UpdateAsync(ProfileTypeDefinitionModel model, UserToken userToken)
        {
            Expression<Func<ProfileTypeDefinition, bool>> filterExpression = GetIdentityExpression(model);

            var entity = base.FindByCondition(userToken, filterExpression)
                .Include(p => p.Interfaces)
                .Include(p => p.Compositions)
                .Include(p => p.Attributes).ThenInclude(a => a.DataType)
                .FirstOrDefault();
            entity.UpdatedById = userToken.UserId;
            entity.Updated = DateTime.UtcNow;

            this.MapToEntity(ref entity, model, userToken);

            await _repo.UpdateAsync(entity);

            return entity.ID;
        }

        private static Expression<Func<ProfileTypeDefinition, bool>> GetIdentityExpression(ProfileTypeDefinitionModel model)
        {
            Expression<Func<ProfileTypeDefinition, bool>> filterExpression;
            if ((model.ID ?? 0) != 0)
            {
                filterExpression = pi => pi.ID == model.ID;
            }
            else
            {
                filterExpression = pi => pi.OpcNodeId == model.OpcNodeId && pi.Profile.Namespace == model.Profile.Namespace && pi.Profile.PublishDate == model.Profile.PublishDate && pi.Profile.Version == model.Profile.Version;
            }

            return filterExpression;
        }
        private static bool MatchIdentity(int? entityId, string entityOpcNodeId, string entityNamespace, int? modelId, string modelOpcNodeId, string modelNamespace)
        {
            if ((modelId ?? 0) != 0 && (entityId ?? 0) != 0)
            {
                return modelId == entityId;
            }
            else
            {
                return (modelOpcNodeId != null || entityOpcNodeId != null) && modelOpcNodeId == entityOpcNodeId && modelNamespace == entityNamespace;
            }
        }
        private static bool MatchIdentity(ProfileTypeDefinition entity, ProfileTypeDefinitionModel model)
        {
            return MatchIdentity(entity.ID, entity.OpcNodeId, entity.Profile.Namespace, model.ID, model.OpcNodeId, model.Profile.Namespace);
        }
        private static bool MatchIdentity(ProfileComposition entity, ProfileTypeDefinitionRelatedModel model)
        {
            // Compositions don't have a nodeid, matching happens on BrowseName
            return MatchIdentity(entity.ID, entity.BrowseName, null, model.ID, model.BrowseName, null);
        }
        private static bool MatchIdentity(ProfileAttribute entity, ProfileAttributeModel model)
        {
            return MatchIdentity(entity.ID, entity.OpcNodeId, entity.Namespace, model.ID, model.OpcNodeId, model.Namespace)
                && ((model.ID ?? 0) != 0 || (entity.Name == model.Name && entity.BrowseName == model.BrowseName));
        }

        public override ProfileTypeDefinition CheckForExisting(ProfileTypeDefinitionModel model, UserToken userToken, bool cacheOnly = false)
        {
            var identityExpression = GetIdentityExpression(model);
            if (!cacheOnly)
            {
                // Ensure we find any entities that have not been saved yet: queries go only against the database
                var cachedProfile = base.FindByCondition(userToken,
                    identityExpression,
                    true)?.FirstOrDefault();
                if (cachedProfile != null)
                {
                    return cachedProfile;
                }
            }
            var existingProfile = base.FindByCondition(userToken,
                identityExpression,
                cacheOnly)?.FirstOrDefault();
            return existingProfile;
        }

        public override async Task<int?> DeleteAsync(int id, UserToken userToken)
        {
            //can only delete your own stuff
            var entity = base.FindByCondition(userToken, x => x.ID == id && x.OwnerId.Equals(userToken.UserId))
                .Include(p => p.Interfaces)
                //FIX - make sure analytics (child data) are included so that they are deleted when delete is called. 
                .Include(p => p.Analytics)
                .Include(p => p.Compositions)
                .Include(p => p.Attributes)
                .FirstOrDefault();
            if (entity == null) return -1;

            //hard delete
            //Note this deletes the dependent related data also
            return await _repo.DeleteAsync(entity);
        }


        #region Map Entity to MODEL
        public ProfileTypeDefinitionModel MapToModelPublic(ProfileTypeDefinition entity, bool verbose = false)
        {
            return MapToModel(entity, verbose);
        }
        protected override ProfileTypeDefinitionModel MapToModel(ProfileTypeDefinition entity, bool verbose = true)
        {
            if (entity != null)
            {
                var result = new ProfileTypeDefinitionModel
                {
                    ID = entity.ID,
                    OpcNodeId = entity.OpcNodeId,
                    Name = entity.Name,
                    ProfileId = entity.ProfileId != 0 ? entity.ProfileId : null,
                    Profile = MapToModelProfile(entity.Profile),
                    BrowseName = entity.BrowseName,
                    SymbolicName = entity.SymbolicName,
                    Description = entity.Description,
                    TypeId = entity.ProfileTypeId,
                    Type = entity.ProfileType != null ?
                        new LookupItemModel { ID = entity.ProfileType.ID, Name = entity.ProfileType.Name, TypeId = entity.ProfileType.ID }
                        : null,
                    AuthorId = entity.AuthorId ?? null,
                    Author = MapToModelSimpleUser(entity.Author),
                    ExternalAuthor = entity.ExternalAuthor,
                    DocumentUrl = entity.DocumentUrl,
                    IsAbstract = entity.IsAbstract,
                    Created = entity.Created,
                    Updated = entity.Updated,
                    MetaTags = string.IsNullOrEmpty(entity.MetaTags) ? new List<string>() : JsonConvert.DeserializeObject<List<MetaTag>>(entity.MetaTags).Select(s => s.Name.Trim()).ToList(),
                    MetaTagsConcatenated = string.IsNullOrEmpty(entity.MetaTags) ? "" : string.Join(", ", Enumerable.ToArray(JsonConvert.DeserializeObject<List<MetaTag>>(entity.MetaTags).Select(s => s.Name.Trim()))),
                    IsActive = entity.IsActive,
                    IsFavorite = entity.Favorite != null,
                    //calculated value which gives more emphasis on extending an item
                    PopularityIndex = MapToModelPopularityIndex(entity)

                };

                //when getting a list of profiles, we need not get all of this stuff
                //only get this when getting individual profiles
                if (verbose)
                {
                    result.Parent = MapToModelProfileTypDefSimple(entity.Parent);
                    result.InstanceParent = MapToModel(entity.InstanceParent, false);
                    result.IsOptionSet = entity.IsOptionSet;
                    result.VariableDataType = MapToModel(entity.VariableDataType);
                    result.VariableValueRank = entity.VariableValueRank;
                    result.VariableArrayDimensions = entity.VariableArrayDimensions;
                    result.VariableValue = entity.VariableValue;
                    result.Attributes = MapToModelAttributes(entity);
                    result.Interfaces = MapToModelInterfaces(entity.Interfaces);
                    result.Compositions = MapToModelCompositions(entity.Compositions, result);
                    result.UpdatedBy = MapToModelSimpleUser(entity.UpdatedBy);
                    result.CreatedBy = MapToModelSimpleUser(entity.CreatedBy);
                }
                return result;
            }
            else
            {
                return null;
            }

        }

        protected static int MapToModelPopularityIndex(ProfileTypeDefinition entity)
        {
            if (entity != null)
            {
                //10 pts for favorite - specific to each user so count does not cross boundaries
                //3 pts for each extend usage - system wide
                //1 pt for each page visit count
                var result = entity.Favorite != null && entity.Favorite.IsFavorite ? 10 : 0;
                result += entity.Analytics == null ? 0 :
                        3 * entity.Analytics.ExtendCount + entity.Analytics.PageVisitCount
                        + entity.Analytics.ManualRank;
                return result;
            }
            else
            {
                return 0;
            }

        }

        protected static ProfileModel MapToModelProfile(Profile entity)
        {
            if (entity != null)
            {
                var result = new ProfileModel
                {
                    ID = entity.ID,
                    Namespace = entity.Namespace,
                    CloudLibraryId = entity.CloudLibraryId,
                    CloudLibPendingApproval = entity.CloudLibPendingApproval,
                    Version = entity.Version,
                    XmlSchemaUri = entity.XmlSchemaUri,
                    PublishDate = entity.PublishDate,
                    AuthorId = entity.AuthorId,
                    Author = MapToModelSimpleUser(entity.Author)
                    //for space saving, performance, don't populate these values in this scenario
                    //FileCache = entity.NodeSetId,  
                };
                return result;
            }
            else
            {
                return null;
            }

        }

        protected static ProfileTypeDefinitionSimpleModel MapToModelProfileTypDefSimple(ProfileTypeDefinition entity)
        {
            if (entity != null)
            {
                return new ProfileTypeDefinitionSimpleModel
                {
                    ID = entity.ID,
                    OpcNodeId = entity.OpcNodeId,
                    Name = entity.Name,
                    BrowseName = entity.BrowseName,
                    SymbolicName = entity.SymbolicName,
                    DocumentationUrl = entity.DocumentUrl,
                    MetaTags = entity.MetaTags != null ? JsonConvert.DeserializeObject<List<MetaTag>>(entity.MetaTags).Select(s => s.Name.Trim()).ToList() : new(),
                    ProfileId = entity.ProfileId,
                    Profile = MapToModelProfile(entity.Profile),
                    IsAbstract = entity.IsAbstract,
                    Description = entity.Description,
                    Type = entity.ProfileType != null ?
                        new LookupItemModel { ID = entity.ProfileType.ID, Name = entity.ProfileType.Name, TypeId = (int)LookupTypeEnum.ProfileType }
                        : new LookupItemModel { ID = entity.ProfileTypeId }, // CODE REVIEW: ist the Name required anywhere? Should we do a lookup here?
                    Author = MapToModelSimpleUser(entity.Author),
                    VariableDataTypeId = entity.VariableDataTypeId,
                };
            }
            else
            {
                return null;
            }

        }

        private List<ProfileAttributeModel> MapToModelAttributes(ProfileTypeDefinition entity)
        {
            //sort by enum value if present, then by name
            if (entity.Attributes == null) return null;
            return entity.Attributes
                .OrderByDescending(a => a.EnumValue.HasValue)
                .ThenBy(a => a.EnumValue)
                .ThenBy(a => a.Name)
                .Select(a => MapToModelAttribute(entity.ID, a))
                .ToList();
        }


        private ProfileAttributeModel MapToModelAttribute(int? typeDefinitionId, ProfileAttribute item)
        {
            return new ProfileAttributeModel()
            {
                ID = item.ID,
                OpcNodeId = item.OpcNodeId,
                BrowseName = item.BrowseName,
                SymbolicName = item.SymbolicName,
                Namespace = item.Namespace,
                Name = item.Name,
                Description = item.Description,
                DisplayName = item.DisplayName,
                Created = item.Created,
                CreatedBy = item.CreatedBy == null ? null : new UserSimpleModel { ID = item.CreatedBy.ID, ObjectIdAAD = item.CreatedBy.ObjectIdAAD, DisplayName = item.CreatedBy.DisplayName },
                Updated = item.Updated,
                UpdatedBy = item.UpdatedBy == null ? null : new UserSimpleModel { ID = item.UpdatedBy.ID, ObjectIdAAD = item.UpdatedBy.ObjectIdAAD, DisplayName = item.UpdatedBy.DisplayName },
                VariableTypeDefinitionId = item.VariableTypeDefinitionId,
                VariableTypeDefinition = MapToModel(item.VariableTypeDefinition, false),
                TypeDefinitionId = typeDefinitionId,
                DataTypeId = item.DataType?.ID,
                DataVariableNodeIds = item.DataVariableNodeIds,
                AttributeType = item.AttributeType != null ? new LookupItemModel() { ID = item.AttributeType.ID, Name = item.AttributeType.Name, Code = item.AttributeType.Code } : null,
                //TBD - come back to this...mapping
                DataType = MapToModelDataType(item),
                EngUnit = !item.EngUnitId.HasValue ? null : _euDAL.MapToModelPublic(item.EngUnit, true),
                EngUnitOpcNodeId = item.EngUnitOpcNodeId,
                EngUnitModelingRule = item.EngUnitModelingRule,
                EngUnitAccessLevel = item.EngUnitAccessLevel,
                MinimumSamplingInterval = item.MinimumSamplingInterval,

                MinValue = item.MinValue,
                MaxValue = item.MaxValue,
                EURangeOpcNodeId = item.EURangeOpcNodeId,
                EURangeModelingRule = item.EURangeModelingRule,
                EURangeAccessLevel = item.EURangeAccessLevel,

                InstrumentMinValue = item.InstrumentMinValue,
                InstrumentMaxValue = item.InstrumentMaxValue,
                InstrumentRangeOpcNodeId = item.InstrumentRangeOpcNodeId,
                InstrumentRangeModelingRule = item.InstrumentRangeModelingRule,
                InstrumentRangeAccessLevel = item.InstrumentRangeAccessLevel,
                EnumValue = item.EnumValue,
                IsRequired = item.IsRequired,
                ModelingRule = item.ModelingRule,
                IsArray = item.IsArray,
                ValueRank = item.ValueRank,
                ArrayDimensions = item.ArrayDimensions,
                MaxStringLength = item.MaxStringLength,
                //// Stored as JSON so return as JRaw, however, check to confirm there is a value first before trying to pass null.
                //TODO: SC - come back to this. Web API not liking JRAW type as part of model.
                //AdditionalData = !string.IsNullOrEmpty(item.AdditionalData) ? new JRaw(item.AdditionalData) : null,

                AccessLevel = item.AccessLevel,
                // deprecated: UserAccessLevel = item.UserAccessLevel,
                AccessRestrictions = item.AccessRestrictions,
                WriteMask = item.WriteMask,
                UserWriteMask = item.WriteMask,

                AdditionalData = item.AdditionalData,
                IsActive = item.IsActive
            };

        }

        /// <summary>
        /// Re-factor this into its own method to address code smell
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private LookupDataTypeModel MapToModelDataType(ProfileAttribute item)
        {
            if (item.DataType == null) return null;

            return new LookupDataTypeModel
            {
                ID = item.DataType.ID,
                Name = item.DataType.Name,
                Code = item.DataType.Code,
                CustomTypeId = item.DataType.CustomTypeId,
                CustomType = item.DataType.CustomType != null ?
                             MapToModel(item.DataType.CustomType, false) : null,
            };
        }

        private List<ProfileTypeDefinitionModel> MapToModelInterfaces(List<ProfileInterface> interfaces)
        {
            if (interfaces == null) return null;
            var result = interfaces.OrderBy(i => i.Interface.Name)
                .Select((i, index) =>
                {
                    var typeModel = new ProfileTypeDefinitionModel
                    {
                        ID = i.InterfaceId,
                        OpcNodeId = i.Interface.OpcNodeId,
                        ProfileId = i.Interface.ProfileId != 0 ? i.Interface.ProfileId : null,
                        Profile = _profileDAL.MapToModelPublic(i.Interface.Profile, true),
                        Name = i.Interface.Name,
                        BrowseName = i.Interface.BrowseName,
                        SymbolicName = i.Interface.SymbolicName,
                        AuthorId = 99,  //just populate with some value for now to prevent model state errors on save. 
                        Description = i.Interface.Description,
                        DocumentUrl = i.Interface.DocumentUrl,
                        TypeId = i.Interface.ProfileTypeId,
                        Type = new LookupItemModel { ID = i.Interface.ProfileType?.ID, Name = i.Interface.ProfileType?.Name },
                        //just get the id for navigating up the ancestory tree
                        Parent = !i.Interface.ParentId.HasValue ? null : new ProfileTypeDefinitionSimpleModel() { ID = i.Interface.ParentId.Value },
                        Attributes = MapToModelInterfaceAttributes(i.Interface, index),
                        //Compositions - added below
                        IsAbstract = i.Interface.IsAbstract,
                    };
                    typeModel.Compositions = MapToModelCompositions(i.Interface.Compositions, typeModel);
                    return typeModel;
                }

                ).ToList();
            return result;
        }

        /// <summary>
        /// </summary>
        /// <remarks>
        /// TBD - Come back to this and improve encapsulation of this and MapToModelAttributes.
        /// </remarks>
        /// <param name="parentId"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        private List<ProfileAttributeModel> MapToModelInterfaceAttributes(ProfileTypeDefinition entity, int groupId)
        {
            if (entity.Attributes == null) return null;
            var result = entity.Attributes.OrderBy(a => a.Name)
                .Select(a => MapToModelAttribute(entity.ID, a))
                .ToList();

            //add some interface specific stuff for front end
            foreach (var a in result)
            {
                a.Interface = new ProfileTypeDefinitionRelatedModel() { ID = entity.ID, Name = entity.Name, BrowseName = entity.BrowseName, SymbolicName = entity.SymbolicName, OpcNodeId = entity.OpcNodeId };
                a.InterfaceGroupId = groupId;
            }
            return result;

        }

        private List<ProfileTypeDefinitionRelatedModel> MapToModelCompositions(List<ProfileComposition> compositions, ProfileTypeDefinitionModel composingModel)
        {
            if (compositions == null) return null;
            var result = compositions.OrderBy(i => i.Composition.Name)
                .Select(i =>
                {
                    ProfileTypeDefinitionModel intermediateObject, relatedType;
                    if (GenerateIntermediateCompositionObjects && i.Composition.ProfileTypeId == (int) ProfileItemTypeEnum.Object && i.Composition?.Parent?.ProfileTypeId == (int) ProfileItemTypeEnum.Class)
                    {
                        intermediateObject = MapToModel(i.Composition, false);
                        relatedType = MapToModel(i.Composition.Parent, false);
                    }
                    else
                    {
                        intermediateObject = null;
                        relatedType = MapToModel(i.Composition, false);
                    }
                    var comp = new ProfileTypeDefinitionRelatedModel
                    {
                        ID = i.ID, //composingModel.ID,
                        ProfileTypeDefinition = composingModel,
                        Name = i.Name,
                        OpcNodeId = i.OpcNodeId,
                        BrowseName = i.BrowseName,
                        SymbolicName = i.SymbolicName,
                        DocumentationUrl = i.DocumentUrl,
                        MetaTags = i.MetaTags != null ? JsonConvert.DeserializeObject<List<MetaTag>>(i.MetaTags)?.Select(s => s.Name.Trim()).ToList() : new(),
                        Profile = composingModel.Profile,

                        IsRequired = i.IsRequired,
                        ModelingRule = i.ModelingRule,
                        RelatedIsEvent = i.IsEvent,
                        Description = i.Description,
                        IntermediateObjectId = intermediateObject?.ID,
                        IntermediateObject = intermediateObject,
                        IntermediateObjectName = intermediateObject?.Name,
                        RelatedProfileTypeDefinitionId = relatedType?.ID,
                        RelatedProfileTypeDefinition = relatedType,
                        //RelatedName = i.Composition.Name,
                        //RelatedDescription = i.Composition.Description,
                        RelatedReferenceId = i.ReferenceId,
                        RelatedReferenceIsInverse = i.ReferenceIsInverse,
                        Type = i.Composition.ProfileType != null ? new LookupItemModel { ID = i.Composition.ProfileType.ID, Name = i.Composition.ProfileType.Name } : new LookupItemModel { ID = i.Composition.ProfileTypeId },
                    };
                    return comp;
                }).ToList();
            return result;
        }

        #endregion

        #region Map MODEL to Entity
        public void MapToEntityPublic(ref ProfileTypeDefinition entity, ProfileTypeDefinitionModel model, UserToken userToken)
        {
            MapToEntity(ref entity, model, userToken);
        }
        protected override void MapToEntity(ref ProfileTypeDefinition entity, ProfileTypeDefinitionModel model, UserToken userToken)
        {
            //updated by, created by - set in base class.
            entity.Name = model.Name;
            if (string.IsNullOrEmpty(model.OpcNodeId))// && !string.IsNullOrEmpty(model.Profile?.Namespace))
            {
                model.OpcNodeId = $"g={Guid.NewGuid()}";
                // TODO find the highest used node id: not just in profile type definitions but also attributes, data types, engineering units etc., so this is insufficient
                //var highestNodeId = this.Where(pd => pd.Profile.Namespace == model.Profile.Namespace, userToken).Data.OrderByDescending(pd => pd.OpcNodeId).FirstOrDefault();
                //if (highestNodeId?.OpcNodeId != null && highestNodeId.OpcNodeId.StartsWith("i=") && int.TryParse(highestNodeId.OpcNodeId.Substring("i=".Length), out var nodeIdNumber))
                //{
                //    highestNodeId.OpcNodeId = $"i={nodeIdNumber+1}";
                //}
                //else
                //{
                //    model.OpcNodeId = "i=5000";
                //}
            }
            entity.OpcNodeId = model.OpcNodeId;
            entity.ProfileId = model.ProfileId != 0 ? model.ProfileId : null;
            if (model.Profile != null)
            {
                var profileEntity = entity.Profile;
                if (profileEntity == null)
                {
                    profileEntity = _profileDAL.CheckForExisting(model.Profile, userToken);
                    if (profileEntity == null)
                    {
                        throw new NotImplementedException($"Profiles must be added explicitly");
                    }
                }
                entity.Profile = profileEntity;
            }
            entity.BrowseName = model.BrowseName;
            entity.SymbolicName = model.SymbolicName;
            entity.ProfileTypeId = model.TypeId;
            entity.Description = model.Description;
            entity.DocumentUrl = model.DocumentUrl;
            entity.IsAbstract = model.IsAbstract;
            entity.Updated = DateTime.UtcNow;

            // TODO any other columns that need to be mapped?

            entity.AuthorId = model.AuthorId;
            entity.ExternalAuthor = model.ExternalAuthor;
            if (CheckForExisting(model, userToken, true) == null)
            {
                _repo.Attach(entity);  // Attach to context so CheckForExisting can find it if there are recursive references in subsequent mapping operations
            }
            entity.ParentId = model.Parent?.ID != 0 ? model.Parent?.ID : null;
            if (model.Parent?.ProfileTypeDefinition != null)
            {
                var parentProfileEntity = entity.Parent;
                if (parentProfileEntity == null)
                {
                    parentProfileEntity = CheckForExisting(model.Parent.ProfileTypeDefinition, userToken);
                    if (parentProfileEntity == null)
                    {
                        this._diLogger.LogTrace($"Creating parent profile type {model.Parent.ProfileTypeDefinition} as side effect of creating {model}");
                        this.AddAsync(model.Parent.ProfileTypeDefinition, userToken).Wait();
                        parentProfileEntity = CheckForExisting(model.Parent.ProfileTypeDefinition, userToken);
                    }
                    entity.Parent = parentProfileEntity;
                }
            }

            entity.InstanceParentId = model.InstanceParent?.ID != 0 ? model.InstanceParent?.ID : null;
            if (model.InstanceParent != null)
            {
                var instanceParentEntity = entity.InstanceParent;
                if (instanceParentEntity == null)
                {
                    if (model.InstanceParent.ProfileId != null && model.InstanceParent.Profile == null)
                    {
                        model.InstanceParent.Profile = _profileDAL.GetById(model.InstanceParent.ProfileId.Value, userToken);
                    }
                    instanceParentEntity = CheckForExisting(model.InstanceParent, userToken);
                    if (instanceParentEntity == null)
                    {
                        this.AddAsync(model.InstanceParent, userToken).Wait();
                        instanceParentEntity = CheckForExisting(model.InstanceParent, userToken);
                    }
                    entity.InstanceParent = instanceParentEntity;
                }
                if (instanceParentEntity != null)
                {
                    _diLogger.LogInformation($"Instance parent {model.InstanceParent} in {model} has an entity and was updated.");
                }
            }

            entity.VariableDataTypeId = (model.VariableDataType?.ID ?? 0) != 0 ? model.VariableDataType.ID : null;
            if (model.VariableDataType != null)
            {
                var variableDataTypeEntity = entity.VariableDataType;
                if (variableDataTypeEntity == null)
                {
                    variableDataTypeEntity = CheckForExisting(model.VariableDataType, userToken);
                    if (variableDataTypeEntity == null)
                    {
                        this.AddAsync(model.VariableDataType, userToken).Wait();
                        variableDataTypeEntity = CheckForExisting(model.VariableDataType, userToken);
                    }
                    entity.VariableDataType = variableDataTypeEntity;
                }
                if (variableDataTypeEntity != null)
                {
                    _diLogger.LogInformation($"Variable Data Type  {model.VariableDataType} in {model} has an entity and was updated.");
                }
            }
            entity.VariableValueRank = model.VariableValueRank;
            entity.IsOptionSet = model.IsOptionSet;
            entity.VariableArrayDimensions = model.VariableArrayDimensions;
            entity.VariableValue = model.VariableValue;

            //favorite
            MapToEntityFavorite(ref entity, model, userToken);

            MapToEntityProfileAttribute(ref entity, model.Attributes, userToken);
            MapToEntityInterfaces(ref entity, model.Interfaces, userToken);
            MapToEntityCompositions(ref entity, model.Compositions, userToken);
            MapToEntityMetaTags(ref entity, model.MetaTags);
        }

        protected static void MapToEntityFavorite(ref ProfileTypeDefinition entity, ProfileTypeDefinitionModel model, UserToken userToken)
        {
            if (model.IsFavorite.HasValue && model.IsFavorite.Value)
            {
                entity.Favorite ??= new ProfileTypeDefinitionFavorite() { OwnerId = userToken.UserId, ProfileTypeDefinitionId = model.ID.Value };
                entity.Favorite.IsFavorite = model.IsFavorite.Value;
            }
            else
            {
                entity.Favorite = null;
            }
        }

        protected void MapToEntityProfileAttribute(ref ProfileTypeDefinition entity, List<Models.ProfileAttributeModel> attributes, UserToken userToken)
        {
            //init visit services for new scenario
            if (entity.Attributes == null) entity.Attributes = new List<ProfileAttribute>();

            // Remove attribs no longer used
            // Use counter from end of collection so we can remove and not mess up loop iterator 
            if (entity.Attributes.Count > 0)
            {
                var length = entity.Attributes.Count - 1;
                for (var i = length; i >= 0; i--)
                {
                    var current = entity.Attributes[i];

                    //remove if no longer present
                    var source = attributes?.Find(x =>
                        MatchIdentity(current, x)
                        );
                    if (source == null)
                    {
                        entity.Attributes.RemoveAt(i);
                    }
                    else
                    {
                        //update if present
                        current.Name = source.Name;
                        current.BrowseName = source.BrowseName;
                        current.SymbolicName = source.SymbolicName;
                        if (string.IsNullOrEmpty(source.OpcNodeId)/* && !string.IsNullOrEmpty(source...Profile?.Namespace)*/)
                        {
                            source.OpcNodeId = $"g={Guid.NewGuid()}";
                        }
                        current.OpcNodeId = source.OpcNodeId;
                        current.Namespace = source.Namespace;
                        current.Description = source.Description;
                        current.DisplayName = source.DisplayName;
                        current.Updated = DateTime.UtcNow;
                        current.UpdatedById = userToken.UserId;
                        current.EngUnitId = source.EngUnit?.ID != 0 ? source.EngUnit?.ID : null;
                        current.EngUnitOpcNodeId = source.EngUnitOpcNodeId;
                        current.EngUnitModelingRule = source.EngUnitModelingRule;
                        current.EngUnitAccessLevel = source.EngUnitAccessLevel;
                        current.EURangeOpcNodeId = source.EURangeOpcNodeId;
                        current.EURangeModelingRule = source.EURangeModelingRule;
                        current.EURangeAccessLevel = source.EURangeAccessLevel;
                        current.MinimumSamplingInterval = source.MinimumSamplingInterval;

                        current.MinValue = source.MinValue;
                        current.MaxValue = source.MaxValue;
                        current.EURangeOpcNodeId = source.EURangeOpcNodeId;
                        current.EURangeModelingRule = source.EURangeModelingRule;
                        current.EURangeAccessLevel = source.EURangeAccessLevel;

                        current.InstrumentMinValue = source.InstrumentMinValue;
                        current.InstrumentMaxValue = source.InstrumentMaxValue;
                        current.InstrumentRangeOpcNodeId = source.InstrumentRangeOpcNodeId;
                        current.InstrumentRangeModelingRule = source.InstrumentRangeModelingRule;
                        current.InstrumentRangeAccessLevel = source.InstrumentRangeAccessLevel;

                        current.DataTypeId = source.DataType?.ID != 0 ? source.DataType.ID : null;
                        current.DataVariableNodeIds = source.DataVariableNodeIds;
                        var dataType = current.DataType;
                        if (dataType == null || dataType.ID != source.DataType.ID)
                        {
                            dataType = _dataTypeDAL.CheckForExisting(source.DataType, userToken);
                            if (dataType == null)
                            {
                                throw new UnresolvedDataTypeException($"Unable to resolve data type {source.DataType} in {source} ");
                            }
                            current.DataType = dataType;
                        }

                        current.VariableTypeDefinitionId = source.VariableTypeDefinition?.ID != 0 ? source.VariableTypeDefinitionId : null;
                        var variableType = current.VariableTypeDefinition;
                        if (source.VariableTypeDefinition != null)
                        {
                            if (variableType == null || variableType.ID != source.VariableTypeDefinition.ID)
                            {
                                variableType = CheckForExisting(source.VariableTypeDefinition, userToken);
                                if (variableType == null)
                                {
                                    throw new UnresolvedVariableTypeException($"Unable to resolve variable type {source.VariableTypeDefinition} in {source} ");
                                }
                            }
                        }
                        else
                        {
                            variableType = null;
                        }
                        current.VariableTypeDefinition = variableType;
                        if (source.AttributeType?.ID != null)
                        {
                            current.AttributeTypeId = source.AttributeType.ID;
                        }
                        current.EnumValue = source.EnumValue;
                        current.IsRequired = source.IsRequired;
                        current.ModelingRule = source.ModelingRule;
                        current.IsArray = source.IsArray;
                        current.ValueRank = source.ValueRank;
                        current.ArrayDimensions = source.ArrayDimensions;
                        current.MaxStringLength = source.MaxStringLength;
                        current.AccessLevel = source.AccessLevel;
                        // deprecated: current.UserAccessLevel = source.UserAccessLevel;
                        current.AccessRestrictions = source.AccessRestrictions;
                        current.WriteMask = source.WriteMask;
                        current.UserWriteMask = source.UserWriteMask;

                        current.AdditionalData = source.AdditionalData;
                    }
                }
            }

            // Loop over attribs passed in and only add those not already there
            if (attributes != null)
            {
                foreach (var attr in attributes)
                {
                    if (
                        entity.Attributes.Find(up => MatchIdentity(up, attr)) == null
                        )
                    {
                        var dataType = _dataTypeDAL.CheckForExisting(attr.DataType, userToken);
                        if (dataType == null)
                        {
                            attr.DataType.ID = _dataTypeDAL.AddAsync(attr.DataType, userToken).Result;
                            dataType = _dataTypeDAL.CheckForExisting(attr.DataType, userToken);
                        }
                        ProfileTypeDefinition variableType = null;
                        if (attr.VariableTypeDefinition != null)
                        {
                            variableType = CheckForExisting(attr.VariableTypeDefinition, userToken);
                            if (variableType == null)
                            {
                                this._diLogger.LogTrace($"Creating variable type {attr.VariableTypeDefinition} as side effect of creating {attr}");
                                this.AddAsync(attr.VariableTypeDefinition, userToken).Wait();
                                variableType = CheckForExisting(attr.VariableTypeDefinition, userToken);
                            }
                        }

                        EngineeringUnit engUnit = null;
                        if (attr.EngUnit != null)
                        {
                            engUnit = _euDAL.CheckForExisting(attr.EngUnit, userToken);
                            if (engUnit == null)
                            {
                                throw new InvalidOperationException($"Engineering unit must be explicitly created: {attr.EngUnit} for {entity}");
                            }
                        }

                        entity.Attributes.Add(new ProfileAttribute
                        {
                            Name = attr.Name,
                            BrowseName = attr.BrowseName,
                            SymbolicName = attr.SymbolicName,
                            OpcNodeId = attr.OpcNodeId,
                            Namespace = attr.Namespace,
                            ProfileTypeDefinitionId = entity.ID,
                            Description = attr.Description,
                            DisplayName = attr.DisplayName,
                            Created = DateTime.UtcNow,
                            CreatedById = userToken.UserId,
                            Updated = DateTime.UtcNow,
                            UpdatedById = userToken.UserId,

                            MinValue = attr.MinValue,
                            MaxValue = attr.MaxValue,
                            EURangeOpcNodeId = attr.EURangeOpcNodeId,
                            EURangeModelingRule = attr.EURangeModelingRule,
                            EURangeAccessLevel = attr.EURangeAccessLevel,

                            InstrumentMinValue = attr.InstrumentMinValue,
                            InstrumentMaxValue = attr.InstrumentMaxValue,
                            InstrumentRangeOpcNodeId = attr.InstrumentRangeOpcNodeId,
                            InstrumentRangeModelingRule = attr.InstrumentRangeModelingRule,
                            InstrumentRangeAccessLevel = attr.InstrumentRangeAccessLevel,

                            DataTypeId = attr.DataType.ID,
                            DataType = dataType,
                            DataVariableNodeIds = attr.DataVariableNodeIds,
                            VariableTypeDefinitionId = attr.VariableTypeDefinitionId,
                            VariableTypeDefinition = variableType,
                            AttributeTypeId = attr.AttributeType?.ID ?? 0,
                            EnumValue = attr.EnumValue,
                            IsRequired = attr.IsRequired,
                            ModelingRule = attr.ModelingRule,
                            IsArray = attr.IsArray,
                            ValueRank = attr.ValueRank,
                            ArrayDimensions = attr.ArrayDimensions,
                            MaxStringLength = attr.MaxStringLength,
                            EngUnitId = attr.EngUnit != null && attr.EngUnit.ID != 0 ? attr.EngUnit.ID : null,
                            EngUnit = engUnit,
                            EngUnitOpcNodeId = attr.EngUnitOpcNodeId,
                            EngUnitModelingRule = attr.EngUnitModelingRule,
                            EngUnitAccessLevel = attr.EngUnitAccessLevel,
                            MinimumSamplingInterval = attr.MinimumSamplingInterval,

                            AccessLevel = attr.AccessLevel,
                            // deprecated: UserAccessLevel = attr.UserAccessLevel,
                            AccessRestrictions = attr.AccessRestrictions,
                            WriteMask = attr.WriteMask,
                            UserWriteMask = attr.UserWriteMask,

                            AdditionalData = attr.AdditionalData,
                            IsActive = true,
                        });
                    }
                }
            }
        }

        protected void MapToEntityInterfaces(ref ProfileTypeDefinition entity, List<ProfileTypeDefinitionModel> interfaces, UserToken userToken)
        {
            //init interfaces obj for new scenario
            if (entity.Interfaces == null) entity.Interfaces = new List<ProfileInterface>();

            if (interfaces == null) return; //this shouldn't happen. If all items removed, then it should be a collection w/ 0 items.

            // Remove items no longer used
            // Use counter from end of collection so we can remove and not mess up loop iterator 
            if (entity.Interfaces.Count > 0)
            {
                var length = entity.Interfaces.Count - 1;
                for (var i = length; i >= 0; i--)
                {
                    var current = entity.Interfaces[i];
                    //remove if no longer present
                    var source = interfaces.Find(v => MatchIdentity(current.Interface, v));
                    if (source == null)
                    {
                        entity.Interfaces.RemoveAt(i);
                    }
                    else
                    {
                        //if present, do nothing
                    }
                }
            }

            // Loop over interfaces passed in and only add those not already there
            foreach (var y in interfaces)
            {
                if (
                    entity.Interfaces.Find(x => MatchIdentity(x.Interface, y)) == null
                    )
                {
                    var interfaceEntity = CheckForExisting(y, userToken);
                    if (interfaceEntity == null)
                    {
                        this.AddAsync(y, userToken).Wait();
                        interfaceEntity = CheckForExisting(y, userToken);
                    }
                    if (interfaceEntity == null)
                    {
                        throw new NotImplementedException($"Failed to add interface {y}");
                    }
                    entity.Interfaces.Add(new ProfileInterface
                    {
                        ProfileTypeDefinitionId = entity.ID,
                        ProfileTypeDefinition = entity,
                        InterfaceId = interfaceEntity.ID,
                        Interface = interfaceEntity,
                    });
                }
            }
        }

        protected void MapToEntityCompositions(ref ProfileTypeDefinition entity, List<ProfileTypeDefinitionRelatedModel> compositions, UserToken userToken)
        {
            //init compositions obj for new scenario
            if (entity.Compositions == null) entity.Compositions = new List<Data.Entities.ProfileComposition>();

            if (compositions == null) return; //this shouldn't happen. If all items removed, then it should be a collection w/ 0 items.

            // Remove compositions no longer used
            // Use counter from end of collection so we can remove and not mess up loop iterator 
            if (entity.Compositions.Count > 0)
            {
                var length = entity.Compositions.Count - 1;
                for (var i = length; i >= 0; i--)
                {
                    var currentEntity = entity.Compositions[i];

                    //remove if no longer present
                    var source = compositions.Find(v => MatchIdentity(currentEntity, v));
                    if (source == null)
                    {
                        entity.Compositions.RemoveAt(i);
                        // Delete the composed intermediate object
                        if (GenerateIntermediateCompositionObjects 
                            && currentEntity.Composition.ID != null 
                            && currentEntity.Composition.ProfileTypeId == (int) ProfileItemTypeEnum.Object)
                        {
                            foreach(var composedEntity in currentEntity.Composition.Compositions)
                            {
                                if (composedEntity.Composition.ID != null
                                    && composedEntity.Composition.ProfileTypeId == (int) ProfileItemTypeEnum.Object)
                                {
                                    // Get the entity into EF cache so cascade delete gets it 
                                    var analyticChild = _analyticsDal.Where(x => x.ProfileTypeDefinitionId == composedEntity.Composition.ID.Value, userToken, null, null, false).Data.FirstOrDefault();
                                    DeleteAsync(composedEntity.Composition.ID.Value, userToken).Wait();
                                }
                            }
                            // Get the entity into EF cache so cascade delete gets it 
                            var analytic = _analyticsDal.Where(x => x.ProfileTypeDefinitionId == currentEntity.Composition.ID.Value, userToken, null, null, false).Data.FirstOrDefault();
                            DeleteAsync(currentEntity.Composition.ID.Value, userToken).Wait();
                        }
                    }
                    else
                    {
                        //if present, update name, description
                        var composition = currentEntity;
                        MapToEntityCompositionInternal(ref composition, source, entity, userToken);
                        entity.Compositions[i] = composition;
                    }
                }
            }

            // Loop over compositions passed in and only add those not already there
            foreach (var y in compositions)
            {
                if (entity.Compositions.Find(x => MatchIdentity(x, y)) == null)//(y.ID ?? 0) != 0 ? x.ID.Equals(y.ID) : x.OpcNodeId == y.OpcNodeId && x.ProfileTypeDefinition.Profile.Namespace == y.Profile.Namespace) == null)
                {
                    // -> front end: composition id will be set
                    // -> importer: related id also 0 -> look up based on name/namespace etc.
                    // Add nodeId to profiles etc.
                    var composition = new ProfileComposition();
                    MapToEntityCompositionInternal(ref composition, y, entity, userToken);
                    entity.Compositions.Add(composition);
                }
            }
        }


        private void MapToEntityCompositionInternal(ref ProfileComposition composition, ProfileTypeDefinitionRelatedModel source, ProfileTypeDefinition parentEntity, /*List<(ProfileTypeDefinitionModel Model, ProfileTypeDefinition Entity)> modelsProcessed, */UserToken userToken)
        {
            if (composition.ProfileTypeDefinition != null && composition.ProfileTypeDefinitionId != parentEntity.ID)
            {
                throw new ArgumentException($"Internal error: {composition.ProfileTypeDefinition} does not match {parentEntity}");
            }
            composition.ProfileTypeDefinitionId = parentEntity.ID;
            if (composition.ProfileTypeDefinition == null)
            {
                if (source.ProfileTypeDefinition == null)
                {
                    composition.ProfileTypeDefinition = parentEntity;
                }
                else
                {
                    ProfileTypeDefinition profileTypeDef;
                    if (MatchIdentity(parentEntity, source.ProfileTypeDefinition))//source.ProfileTypeDefinition.OpcNodeId == parentEntity.OpcNodeId && source.ProfileTypeDefinition.Profile.Namespace == parentEntity.Profile.Namespace)
                    {
                        profileTypeDef = parentEntity;
                    }
                    else
                    {
                        profileTypeDef = CheckForExisting(source.ProfileTypeDefinition, userToken);
                        if (profileTypeDef == null)
                        {
                            throw new NotImplementedException("Profile must be added explicitly");
                        }
                    }
                    composition.ProfileTypeDefinition = profileTypeDef;
                    composition.ProfileTypeDefinitionId = profileTypeDef.ID;
                }
            }

            var referenceId = source.RelatedReferenceId;
            System.Diagnostics.Debug.WriteLine($"MapToEntityCompositionInternal(1152): ReferenceId={referenceId} ");

            if (source.IntermediateObjectId != null)
            {
                source.IntermediateObject = GetById(source.IntermediateObjectId.Value, userToken);
            }
            else
            {
                // On add, not all the type info is filled in: read it if available
                if (source.RelatedProfileTypeDefinition?.TypeId == null && source.RelatedProfileTypeDefinitionId != null)
                {
                    source.RelatedProfileTypeDefinition = GetById(source.RelatedProfileTypeDefinitionId.Value, userToken);
                }
                if (GenerateIntermediateCompositionObjects && ProfileMapperUtil.IsHasComponentReference(source.RelatedReferenceId)
                    && source.RelatedProfileTypeDefinition?.TypeId == (int)ProfileItemTypeEnum.Class)
                {
                    if (composition.Composition?.ID != null)
                    {
                        // Use the previously generated intermediate object: frontend sometimes loses the IntermediateObjectId (switch of attribute type)
                        var composedObject = GetById(composition.Composition.ID.Value, userToken);
                        if (composedObject.TypeId == (int)ProfileItemTypeEnum.Object)
                        {
                            source.IntermediateObject = composedObject;
                        }
                    }
                    if (source.IntermediateObject == null)
                    {
                        // Generate intermediate object if related is an ObjectType
                        source.IntermediateObject = new ProfileTypeDefinitionModel
                        {
                            TypeId = (int)ProfileItemTypeEnum.Object,
                            Name = source.Name,
                            BrowseName = source.BrowseName,
                            Author = source.Author ?? MapToModelSimpleUser(composition.ProfileTypeDefinition.Author),
                            AuthorId = source.Author?.ID ?? composition.ProfileTypeDefinition.AuthorId,
                            InstanceParent = MapToModel(composition.ProfileTypeDefinition, false),
                            Parent = ProfileMapperUtil.MapToModelProfileSimple(source.RelatedProfileTypeDefinition),
                            ProfileId = composition.ProfileTypeDefinition.ProfileId,
                            Profile = MapToModelProfile(composition.ProfileTypeDefinition.Profile) ?? _profileDAL.GetById(composition.ProfileTypeDefinition.ProfileId.Value, userToken),
                        };
                        this.AddAsync(source.IntermediateObject, userToken).Wait();
                    }
                }
            }

            if (source.IntermediateObject != null)
            {
                // Update the intermediate object
                var intermediateEntity = CheckForExisting(source.IntermediateObject, userToken);
                composition.Composition = intermediateEntity;
                composition.CompositionId = intermediateEntity.ID;
                if (!string.IsNullOrEmpty(source.Name))
                {
                    // Update the name of the intermediate object
                    intermediateEntity.Name = source.Name;
                }
                if (intermediateEntity.Parent.ID != source.RelatedProfileTypeDefinitionId)
                {
                    // Update the intermediate object's type (Parent)
                    intermediateEntity.ParentId = source.RelatedProfileTypeDefinitionId;
                    var typeEntity = CheckForExisting(source.RelatedProfileTypeDefinition, userToken);
                    intermediateEntity.Parent = typeEntity;
                }
                var customReference = ProfileMapperUtil.GetReferenceTypeForObjectType($"{parentEntity.Parent?.Profile?.Namespace};{parentEntity.Parent?.OpcNodeId}");
                if (!string.IsNullOrEmpty(customReference))
                {
                    // Some object types (FolderType) use special references (Organizes) for compositions
                    referenceId = customReference;
                    System.Diagnostics.Debug.WriteLine($"MapToEntityCompositionInternal(1220): Maybe changed to ReferenceId={referenceId} ");
                }
            }
            else
            {
                composition.CompositionId = source.RelatedProfileTypeDefinitionId; //should be same
                if (composition.Composition == null && source.RelatedProfileTypeDefinition != null)
                {
                    var profileTypeDef = CheckForExisting(source.RelatedProfileTypeDefinition, userToken);
                    if (profileTypeDef == null)
                    {
                        this.AddAsync(source.RelatedProfileTypeDefinition, userToken).Wait();
                        profileTypeDef = CheckForExisting(source.RelatedProfileTypeDefinition, userToken);
                    }
                    composition.Composition = profileTypeDef;
                    composition.CompositionId = profileTypeDef.ID;
                }
            }
            composition.Name = source.Name;
            composition.OpcNodeId = source.OpcNodeId;
            composition.BrowseName = source.BrowseName;
            composition.SymbolicName = source.SymbolicName;
            composition.DocumentUrl = source.DocumentationUrl;

            var entityMetaTags = source.MetaTags?.Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .Select(s => new MetaTag
                { Name = s.Trim() }).ToList() ?? new();
            composition.MetaTags = source.MetaTags != null ? JsonConvert.SerializeObject(entityMetaTags) : null;

            composition.IsRequired = source.IsRequired;
            composition.ModelingRule = source.ModelingRule;
            composition.IsEvent = source.RelatedIsEvent;
            composition.ReferenceId = referenceId;
            composition.ReferenceIsInverse = source.RelatedReferenceIsInverse;
            composition.Description = source.Description;
        }

        protected static void MapToEntityMetaTags(ref ProfileTypeDefinition entity, List<string> metaTags)
        {
            if (metaTags == null || !metaTags.Any())
            {
                entity.MetaTags = null;
            }
            else
            {
                //always swap out the entire list of metatags. trim out whitespaces and any empty tags.
                //these are stored as a simple JSON string of strings
                var result = metaTags.Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s)
                    .Select(s => new MetaTag
                    { Name = s.Trim() }).ToList();

                //convert to json string
                entity.MetaTags = JsonConvert.SerializeObject(result);
            }
        }

        public async Task UpgradeToProfileAsync(ProfileModel profileModel,
            IRepository<ProfileTypeDefinitionAnalytic> ptAnalyticsRepo,
            IRepository<ProfileTypeDefinitionFavorite> ptFavoritesRepo,
            IRepository<LookupDataTypeRanked> dtRankRepo)
        {
            _repo.StartTransaction();
            var profile = _profileDAL.GetRepo().GetAll().FirstOrDefault(p => p.Namespace == profileModel.Namespace && p.PublishDate == profileModel.PublishDate && p.Version == profileModel.Version);
            var profileRepo = _profileDAL.GetRepo();
            var dataTypeRepo = _dataTypeDAL.GetRepo();

            var existingProfiles = profileRepo.GetAll().Where(p => p.Namespace == profile.Namespace).ToList();
            existingProfiles.Remove(profile);
            foreach (var existingProfile in existingProfiles)
            {
                // Update all profile type definitions

                // Parent
                var profileTypes = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace && pt.Parent.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Parent || profileTypes count: {profileTypes.Count()}");
                foreach (var profileType in profileTypes)
                {
                    var newParent = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == profileType.Parent.OpcNodeId);
                    if (profileType.Parent.OpcNodeId != newParent.OpcNodeId || profileType.Parent.Name != newParent.Name)
                    {
                        throw new Exception();
                    }
                    profileType.Parent = newParent;
                    _repo.Update(profileType);
                }

                // InstanceParent
                profileTypes = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace && pt.InstanceParent.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Instance Parent || profileTypes count: {profileTypes.Count()}");
                foreach (var profileType in profileTypes)
                {
                    var newInstanceParent = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == profileType.InstanceParent.OpcNodeId);
                    if (profileType.InstanceParent.OpcNodeId != newInstanceParent.OpcNodeId || profileType.InstanceParent.Name != newInstanceParent.Name)
                    {
                        throw new Exception();
                    }
                    profileType.InstanceParent = newInstanceParent;
                    _repo.Update(profileType);
                }

                // VariableDataTypeId
                profileTypes = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace && pt.VariableDataType.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Variable Data Type || profileTypes count: {profileTypes.Count()}");
                foreach (var profileType in profileTypes)
                {
                    var newVariableType = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == profileType.VariableDataType.OpcNodeId);
                    if (profileType.VariableDataType.OpcNodeId != newVariableType.OpcNodeId || profileType.VariableDataType.Name != newVariableType.Name)
                    {
                        throw new Exception();
                    }
                    profileType.VariableDataType = newVariableType;
                    profileType.VariableDataTypeId = newVariableType?.ID;
                    _repo.Update(profileType);
                }

                // Update all profile attributes
                // data type id
                var attributes = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace)
                    .SelectMany(pt => pt.Attributes).Where(a => a.DataType.CustomType.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Profile Attributes || count: {attributes.Count()}");
                foreach (var attribute in attributes)
                {
                    var newCustomDataType = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == attribute.DataType.CustomType.OpcNodeId);
                    var newDataType = dataTypeRepo.GetAll().FirstOrDefault(dt => dt.CustomType.ID == newCustomDataType.ID);
                    if (newDataType.Name != attribute.DataType.Name || attribute.DataType.CustomType.OpcNodeId != newDataType.CustomType.OpcNodeId || attribute.DataType.Name != newDataType.Name)
                    {
                        throw new Exception();
                    }
                    attribute.DataType = newDataType;
                    _repo.Update(attribute.ProfileTypeDefinition);
                }

                // variable type definition id
                attributes = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace)
                    .SelectMany(pt => pt.Attributes).Where(a => a.VariableTypeDefinition.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Variable Type Definition Attributes || count: {attributes.Count()}");
                foreach (var attribute in attributes)
                {
                    var newVariableType = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == attribute.VariableTypeDefinition.OpcNodeId);
                    if (attribute.VariableTypeDefinition.OpcNodeId != newVariableType.OpcNodeId || attribute.VariableTypeDefinition.Name != newVariableType.Name)
                    {
                        throw new Exception();
                    }
                    attribute.VariableTypeDefinition = newVariableType;
                    _repo.Update(attribute.ProfileTypeDefinition);
                }

                // Update all profile compositions
                // composition
                var compositions = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace)
                    .SelectMany(pt => pt.Compositions).Where(c => c.Composition.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Compositions || count: {compositions.Count()}");
                foreach (var composition in compositions)
                {
                    var newComposition = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == composition.Composition.OpcNodeId);
                    if (composition.Composition.OpcNodeId != newComposition.OpcNodeId || composition.Composition.Name != newComposition.Name)
                    {
                        throw new Exception();
                    }
                    composition.Composition = newComposition;
                    _repo.Update(composition.ProfileTypeDefinition);
                }


                // Update all profile interfaces
                // interface_id
                var interfaces = _repo.GetAll().Where(pt => pt.Profile.Namespace != existingProfile.Namespace)
                    .SelectMany(pt => pt.Interfaces).Where(itf => itf.Interface.Profile == existingProfile).ToList();
                _logger.Info($"Upgrade Info || Interfaces || count: {interfaces.Count()}");
                foreach (var itf in interfaces)
                {
                    var newInterface = _repo.GetAll().FirstOrDefault(pt => pt.Profile == profile && pt.OpcNodeId == itf.Interface.OpcNodeId);
                    if (itf.Interface.OpcNodeId != newInterface.OpcNodeId || itf.Interface.Name != newInterface.Name)
                    {
                        throw new Exception();
                    }
                    itf.Interface = newInterface;
                    _repo.Update(itf.ProfileTypeDefinition);
                }

                // Move over user analytics
                {
                    var ptAnalyticsRecords = ptAnalyticsRepo.GetAll().Where(pta => pta.ProfileTypeDefinition.Profile == existingProfile).ToList();
                    _logger.Info($"Upgrade Info || Analytics || count: {ptAnalyticsRecords.Count()}");
                    foreach (var ptAnalyticsRecord in ptAnalyticsRecords)
                    {
                        var ptaNew = ptAnalyticsRepo.GetAll().Where(pta => pta.ProfileTypeDefinition.Profile == profile && pta.ProfileTypeDefinition.OpcNodeId == ptAnalyticsRecord.ProfileTypeDefinition.OpcNodeId).FirstOrDefault();
                        if (ptaNew != null)
                        {
                            ptaNew.ManualRank = ptAnalyticsRecord.ManualRank;
                            ptaNew.PageVisitCount = ptAnalyticsRecord.PageVisitCount;
                            ptaNew.ExtendCount = ptAnalyticsRecord.ExtendCount;
                            ptAnalyticsRepo.Update(ptaNew);
                        }
                        else
                        {
                            _logger.Warn($"Upgrade: profile {profile.Namespace} {profile.Version} {profile.PublishDate}: did not find analysics record for {ptAnalyticsRecord.ProfileTypeDefinition.Name} {ptAnalyticsRecord.ProfileTypeDefinition.OpcNodeId} present in {existingProfile.Version} {existingProfile.PublishDate}");
                        }
                    }
                }
                // Move over user favorites
                {
                    var ptFavoritesRecords = ptFavoritesRepo.GetAll().Where(pta => pta.ProfileTypeDefinition.Profile == existingProfile).ToList();
                    _logger.Info($"Upgrade Info || Favorites || count: {ptFavoritesRecords.Count()}");
                    foreach (var ptFavoritesRecord in ptFavoritesRecords)
                    {
                        var ptfNew = ptFavoritesRepo.GetAll().Where(pta => pta.ProfileTypeDefinition.Profile == profile && pta.ProfileTypeDefinition.OpcNodeId == ptFavoritesRecord.ProfileTypeDefinition.OpcNodeId).FirstOrDefault();
                        if (ptfNew != null)
                        {
                            ptfNew.IsFavorite = ptFavoritesRecord.IsFavorite;
                            ptFavoritesRepo.Update(ptfNew);
                        }
                    }
                }
                // Move over data type rank
                // TODO actually migrate this: it is currently just a read-only view at the Repo/EF level so the Add/Update calls are commented out
                {
                    var allDataTypeRanks = dtRankRepo.GetAll().ToList();
                    _logger.Info($"Upgrade Info || Data Type Rank || count: {allDataTypeRanks.Count()}");
                    foreach (var dtRank in allDataTypeRanks)
                    {
                        if (dtRank.CustomTypeId == null)
                        {
                            continue;
                        }
                        var oldDt = dataTypeRepo.GetAll().Where(dt => dt.CustomTypeId == dtRank.CustomTypeId.Value).FirstOrDefault();
                        if (oldDt != null && oldDt.CustomType.Profile == existingProfile)
                        {
                            var newDt = dataTypeRepo.GetAll().Where(dtr => dtr.CustomType.Profile == profile && dtr.CustomType.OpcNodeId == oldDt.CustomType.OpcNodeId).FirstOrDefault();
                            if (newDt == null)
                            {
                                _logger.Warn($"Upgrade: profile {profile.Namespace} {profile.Version} {profile.PublishDate} does not have type {oldDt.Name} {oldDt.CustomType.OpcNodeId} present in {existingProfile.Version} {existingProfile.PublishDate}");
                            }
                            else
                            {
                                var newDtRank = dtRankRepo.GetAll().Where(dtr => dtr.ID == newDt.ID).FirstOrDefault();
                                if (newDtRank == null)
                                {
                                    //newDtRank = new LookupDataTypeRanked
                                    //{
                                    //    ID = newDt.ID,
                                    //    Name = dtRank.Name,
                                    //    OwnerId = dtRank.OwnerId,
                                    //    CustomTypeId = newDt.ID,
                                    //    IsActive = dtRank.IsActive,
                                    //    IsNumeric = newDt.IsNumeric,
                                    //    UseEngUnit = newDt.UseEngUnit,
                                    //    UseMinMax = newDt.UseMinMax,
                                    //    Code = newDt.Code,
                                    //    DisplayOrder = newDt.DisplayOrder,
                                    //};
                                    //dtRankRepo.Add(newDtRank);
                                }
                                else
                                {
                                    //dtRankRepo.Update(newDtRank);
                                }
                                //newDtRank.ManualRank = dtRank.ManualRank;
                                //newDtRank.PopularityIndex = dtRank.PopularityIndex;
                                //newDtRank.PopularityLevel = dtRank.PopularityLevel;
                                //newDtRank.UsageCount = dtRank.UsageCount;
                            }
                        }
                    }
                }

                _logger.Info($"Upgrade Info || SaveChangesAsync...start");
                await _repo.SaveChangesAsync();
                _logger.Info($"Upgrade Info || SaveChangesAsync...complete");
                await _repo.CommitTransactionAsync();
                _logger.Info($"Upgrade Info || CommitTransactionAsync...complete");
            }

        }

        internal void ChangeProfileNamespace(Profile entity, string oldNamespace)
        {
            var profileTypes = _repo.GetAll().Where(pt => pt.ProfileId == entity.ID).ToList();
            foreach (var pt in profileTypes)
            {
                if (pt.BrowseName != null && pt.BrowseName.StartsWith(oldNamespace))
                {
                    pt.BrowseName = entity.Namespace + pt.BrowseName.Substring(oldNamespace.Length);
                }
                var attributesToRename = pt.Attributes;
                foreach (var a in pt.Attributes)
                {
                    a.Namespace = entity.Namespace;
                    a.BrowseName = a.BrowseName?.Replace(oldNamespace, entity.Namespace);
                    a.EngUnitOpcNodeId = a.EngUnitOpcNodeId?.Replace(oldNamespace, entity.Namespace);
                    a.EURangeOpcNodeId = a.EURangeOpcNodeId?.Replace(oldNamespace, entity.Namespace);
                    if (!string.IsNullOrEmpty(a.DataVariableNodeIds))
                    {
                        a.DataVariableNodeIds = a.DataVariableNodeIds?.Replace(oldNamespace, entity.Namespace);
                    }
                }
                foreach (var c in pt.Compositions)
                {
                    c.BrowseName = c.BrowseName?.Replace(oldNamespace, entity.Namespace);
                    c.ReferenceId = c.ReferenceId?.Replace(oldNamespace, entity.Namespace);
                }
                _repo.Update(pt);
            };
            //_repo.SaveChangesAsync().Wait();
        }

        #endregion

    }

    [Serializable]
    public class UnresolvedNodeException : Exception
    {
        public UnresolvedNodeException()
        {
        }

        public UnresolvedNodeException(string message) : base(message)
        {
        }

        public UnresolvedNodeException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnresolvedNodeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class UnresolvedVariableTypeException : UnresolvedNodeException
    {
        public UnresolvedVariableTypeException()
        {
        }

        public UnresolvedVariableTypeException(string message) : base(message)
        {
        }

        public UnresolvedVariableTypeException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnresolvedVariableTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class UnresolvedDataTypeException : UnresolvedNodeException
    {
        public UnresolvedDataTypeException()
        {
        }

        public UnresolvedDataTypeException(string message) : base(message)
        {
        }

        public UnresolvedDataTypeException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnresolvedDataTypeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}