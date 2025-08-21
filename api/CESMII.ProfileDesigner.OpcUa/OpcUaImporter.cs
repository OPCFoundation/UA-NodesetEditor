//#define NODESETDBTEST
namespace CESMII.ProfileDesigner.OpcUa
{
    using CESMII.ProfileDesigner.Common.Enums;
    using CESMII.ProfileDesigner.DAL;
    using CESMII.ProfileDesigner.DAL.Models;
    using CESMII.ProfileDesigner.DAL.Utils;
    using CESMII.ProfileDesigner.Data.Entities;
    using CESMII.OpcUa.NodeSetModel;
    using CESMII.OpcUa.NodeSetModel.Factory.Opc;
    using CESMII.OpcUa.NodeSetModel.Opc.Extensions;
    using CESMII.ProfileDesigner.OpcUa.NodeSetModelFactory.Profile;
    using CESMII.ProfileDesigner.OpcUa.NodeSetModelImport.Profile;
#if NODESETDBTEST
    using Microsoft.EntityFrameworkCore;
#endif
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using global::Opc.Ua;
    using global::Opc.Ua.Export;
    using System.Xml.Serialization;
    using System.Xml;
    using System.Collections.Immutable;
    using CESMII.ProfileDesigner.Api.Shared.Models;
    using CESMII.ProfileDesigner.Opc.Ua.NodeSetDBCache;
    using CESMII.OpcUa.NodeSetImporter;
    using CESMII.Common.CloudLibClient;
    using CESMII.ProfileDesigner.Data.Repositories;

    public class OpcUaImporter
    {
#pragma warning disable S1075 // URIs should not be hardcoded - these are not URLs representing endpoints, but OPC model identifiers (URIs) that are static and stable
        public const string strOpcNamespaceUri = "http://opcfoundation.org/UA/"; //NOSONAR
        public const string strOpcDiNamespaceUri = "http://opcfoundation.org/UA/DI/"; //NOSONAR
#pragma warning restore S1075 // URIs should not be hardcoded

        public OpcUaImporter(
            IDal<ProfileTypeDefinition, ProfileTypeDefinitionModel> dal,
            IDal<LookupDataType, LookupDataTypeModel> dtDal,
            IDal<Profile, ProfileModel> profileDal,
            ICloudLibDal<CloudLibProfileModel> cloudLibDal,
            IUANodeSetResolverWithProgress cloudLibResolver,
            IDal<NodeSetFile, NodeSetFileModel> nodeSetFileDal,
            UANodeSetDBCache nodeSetCache,
            IDal<EngineeringUnit, EngineeringUnitModel> euDal,
            IRepository<ProfileTypeDefinitionAnalytic> ptAnalyticsRepo,
            IRepository<ProfileTypeDefinitionFavorite> ptFavoritesRepo,
            IRepository<LookupDataTypeRanked> dtRankRepo,
            ILogger<OpcUaImporter> logger
#if NODESETDBTEST
            , NodeSetModelContext nsDBContext
#endif
            )
        {
            _dal = dal;
            if (_dal is ProfileTypeDefinitionDAL dalClass)
            {
                dalClass.GenerateIntermediateCompositionObjects = false;
            }
            else
            {
                logger.LogError($"Importer: ProfileTypeDefinitionDAL not of expected type. Import/export may be incorrect.");
            }
            _dtDal = dtDal;
            _euDal = euDal;
            this.Logger = new LoggerCapture(logger);
            this._logger = logger;
#if NODESETDBTEST
            this.nsDBContext = nsDBContext;
#endif
            _profileDal = profileDal;
            _cloudLibDal = cloudLibDal;
            _cloudLibResolver = cloudLibResolver;
            _nodeSetFileDal = nodeSetFileDal;
            _nodeSetCache = nodeSetCache;
            _ptAnalyticsRepo = ptAnalyticsRepo;
            _ptFavoritesRepo = ptFavoritesRepo;
            _dtRankRepo = dtRankRepo;
        }

        public readonly IDal<ProfileTypeDefinition, ProfileTypeDefinitionModel> _dal;
        public readonly IDal<LookupDataType, LookupDataTypeModel> _dtDal;
        public readonly IDal<EngineeringUnit, EngineeringUnitModel> _euDal;
        private readonly IRepository<ProfileTypeDefinitionAnalytic> _ptAnalyticsRepo;
        private readonly IRepository<ProfileTypeDefinitionFavorite> _ptFavoritesRepo;
        private readonly IRepository<LookupDataTypeRanked> _dtRankRepo;
        public readonly LoggerCapture Logger;
        public readonly ILogger _logger;
#if NODESETDBTEST
        private readonly NodeSetModelContext nsDBContext;
#endif
        internal readonly IDal<Data.Entities.Profile, DAL.Models.ProfileModel> _profileDal;
        private readonly ICloudLibDal<CloudLibProfileModel> _cloudLibDal;
        private readonly IUANodeSetResolverWithProgress _cloudLibResolver;
        private readonly IDal<NodeSetFile, NodeSetFileModel> _nodeSetFileDal;
        private readonly UANodeSetDBCache _nodeSetCache;
        readonly Dictionary<string, string> Aliases = new();


        public async Task<List<WarningsByNodeSet>> ImportUaNodeSets(List<ImportOPCModel> nodeSetXmlList, UserToken userToken, Func<string, TaskStatusEnum, Task> logToImportLog, int logId, bool allowMultiVersion, bool upgradePreviousVersions)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogTrace("Starting import");
            var fileNames = string.Join(", ", nodeSetXmlList.Select(f => f.FileName).ToArray<string>());
            var filesImportedMsg = $"Importing Nodeset{(nodeSetXmlList.Count.Equals(1) ? "" : "s")}: {fileNames}";

            _logger.LogInformation($"ImportService|ImportOpcUaProfile|{filesImportedMsg}. User Id:{userToken}.");

            if (_euDal.Count(userToken) == 0)
            {
                await logToImportLog($"Importing engineering units...<br/>{filesImportedMsg}", TaskStatusEnum.InProgress);
                await ImportEngineeringUnitsAsync(UserToken.GetGlobalUser(userToken));
            }
            await logToImportLog($"Validating nodesets and dependencies...<br/>{filesImportedMsg}", TaskStatusEnum.InProgress);

            //init the warnings object outside the try/catch so that saving warnings happens after conclusion of import.
            //We don't want an execption saving warnings to DB to cause a "failed" import message
            //if something goes wrong on the saving of the warnings to the DB, we handle it outside of the import messages.  
            var nodesetWarnings = new List<WarningsByNodeSet>();

            //wrap the importNodesets for total coverage of exceptions
            //we need to inform the front end of an exception and update the import log on 
            //any failure so it can refresh front end accordingly
            //Todo: Revisit this and limit the try/catch blocks
            try
            {
                #region CM new code for importing all NodeSet in correct order and with Dependency Resolution

                _nodeSetCache.SetUser(userToken);
                _profileDal.StartTransaction();
                _logger.LogTrace($"Timestamp||ImportId:{logId}||Importing node sets: {sw.Elapsed}");

                var nodeSetXmlStringList = nodeSetXmlList.Select(nodeSetXml => nodeSetXml.Data).ToList();
                UANodeSetImportResult cachedNodeSetFiles = CacheAndDownloadNodeSetFiles(_nodeSetCache, userToken, nodeSetXmlStringList, logToImportLog);
                _logger.LogTrace($"Timestamp||ImportId:{logId}||Imported node sets: {sw.Elapsed}");
                if (!string.IsNullOrEmpty(cachedNodeSetFiles.ErrorMessage))
                {
                    //The UA Importer encountered a crash/error
                    //failed complete message
                    _profileDal.RollbackTransaction();
                    await logToImportLog(cachedNodeSetFiles.ErrorMessage + $"<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                    return null;
                }
                if (cachedNodeSetFiles?.MissingModels?.Count > 0)
                {
                    //The UA Importer tried to resolve already all missing NodeSet either from Cache or CloudLib but could not find all dependencies
                    //failed complete message
                    _profileDal.RollbackTransaction();
                    var missingModelsText = string.Join(", ", cachedNodeSetFiles.MissingModels);
                    await logToImportLog($"Missing dependent node sets: {missingModelsText}.", TaskStatusEnum.Failed);
                    return null;
                }


                var profilesAndNodeSets = new List<ProfileModelAndNodeSet>();

                //This area will be put in an interface that can be used by the Importer (after Friday Presentation)
                try
                {
                    //_logger.LogTrace($"Timestamp||ImportId:{logId}||Getting standard nodesets: {sw.Elapsed}");
                    //var standardNodeSets = _dalStandardNodeSet.GetAll(userToken);

                    //_logger.LogTrace($"Timestamp||ImportId:{logId}||Verifying standard nodeset: {sw.Elapsed}");
                    //importedNodeSetFiles = UANodeSetValidator.VerifyNodeSetStandard(importedNodeSetFiles, standardNodeSets);
                    //if (!string.IsNullOrEmpty(importedNodeSetFiles?.ErrorMessage))
                    //{
                    //    await logToImportLog(importedNodeSetFiles.ErrorMessage.ToLower() + $"<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                    //    return null;
                    //}

                    if (cachedNodeSetFiles != null && cachedNodeSetFiles.Models.Any())
                    {
                        foreach (var tmodel in cachedNodeSetFiles.Models)
                        {
                            string cloudLibId = null;
                            if (nodeSetXmlList.Count == 1 && nodeSetXmlList[0].CloudLibraryId != null)
                            {
                                if (tmodel.RequestedForThisImport)
                                {
                                    cloudLibId = nodeSetXmlList[0].CloudLibraryId;
                                }
                            }
                            var profile = FindOrCreateProfileForNodeSet(tmodel, cloudLibId, _profileDal, userToken, logId, sw, allowMultiVersion);

                            profilesAndNodeSets.Add(new ProfileModelAndNodeSet
                            {
                                Profile = profile, // TODO use the nodesetfile instead
                                NodeSetModel = tmodel,
                            });

                        }
                    }
                    await logToImportLog($"Nodesets validated.<br/>{filesImportedMsg}", TaskStatusEnum.InProgress);
                }
                catch (Exception e)
                {
                    _nodeSetCache.DeleteNewlyAddedNodeSetsFromCache(cachedNodeSetFiles);
                    //log complete message to logger and abbreviated message to user. 
                    _logger.LogCritical(e, $"ImportId:{logId}||ImportService|ImportOpcUaProfile|{e.Message}");
                    //failed complete message
                    _profileDal.RollbackTransaction();
                    await logToImportLog($"Nodeset validation failed: {e.Message}.<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                    return null;
                }

                if (!profilesAndNodeSets.Any(pn => pn.NodeSetModel.NewInThisImport))
                {
                    await logToImportLog($"Nodeset already imported.", TaskStatusEnum.Failed);
                    return null;
                }

                //To Here
                #endregion

                _logger.LogTrace($"Timestamp||ImportId:{logId}||Starting node import: {sw.Elapsed}");
                await logToImportLog($"Processing nodeset data...<br/>{filesImportedMsg}", TaskStatusEnum.InProgress);
                //shield the front end from an exception message. Catch it, log it, and return success is false w/ simplified message
                try
                {
                    // Performance: Load the profiles and data types into the EF cache, in a singe database query
                    var startEFCache = sw.Elapsed;
                    var profileIds = profilesAndNodeSets.Select(pn => pn.Profile.ID).Where(i => (i ?? 0) != 0).ToList();
                    if (profileIds.Any())
                    {
                        _logger.LogTrace($"Timestamp||ImportId:{logId}||Loading EF cache: {sw.Elapsed}");
                        await _dal.LoadIntoCacheAsync(pt => profileIds.Contains(pt.ProfileId));
                        await _dtDal.LoadIntoCacheAsync(dt => profileIds.Contains(dt.CustomType.ProfileId));
                        var endEFCache = sw.Elapsed;
                        _logger.LogTrace($"Timestamp||ImportId:{logId}||Finished loading EF cache: {endEFCache - startEFCache}");
                    }
                    else
                    {
                        var endEFCache = sw.Elapsed;
                        _logger.LogTrace($"Timestamp||ImportId:{logId}||Not loading EF cache - no required profiles: {endEFCache - startEFCache}");
                    }
                    var nodeSetModels = new Dictionary<string, NodeSetModel>();
                    do
                    {
                        var dalOpcContext = new DalOpcContext(this, nodeSetModels, userToken, userToken, false);
                        var nextImportUri = profilesAndNodeSets.FirstOrDefault(pn => pn.NodeSetModel.NewInThisImport)?.NodeSetModel?.NameVersion.ModelUri;
                        if (nextImportUri != null)
                        {
                            // Ensure the imported namespace is index = 1, so that the JsonEncoder works consistently
                            dalOpcContext.NamespaceUris.GetIndexOrAppend(nextImportUri);
                        }
                        var modelsToImport = new List<NodeSetModel>();
                        foreach (var profileAndNodeSet in profilesAndNodeSets)
                        {
                            //only show message for the items which are newly imported...
                            if (profileAndNodeSet.NodeSetModel.NewInThisImport)
                            {
                                await logToImportLog($"Processing nodeset: {profileAndNodeSet.NodeSetModel.NameVersion}...", TaskStatusEnum.InProgress);
                            }
                            var logList = new List<string>();
                            (Logger as LoggerCapture).LogList = logList;

                            var loadedNodeSetModels = await LoadNodeSetAsync(dalOpcContext, profileAndNodeSet.NodeSetModel.NodeSet, profileAndNodeSet.Profile, !profileAndNodeSet.NodeSetModel.NewInThisImport);
                            if (profileAndNodeSet.NodeSetModel.NewInThisImport)
                            {
                                foreach (var model in loadedNodeSetModels)
                                {
                                    if (modelsToImport.FirstOrDefault(m => m.ModelUri == model.ModelUri) == null)
                                    {
                                        modelsToImport.Add(model);
                                        var itemsByNodeId = await ImportNodeSetModelAsync(model, dalOpcContext, userToken);
                                    }
                                    (Logger as LoggerCapture).LogList = null;
                                    if (logList.Any())
                                    {
                                        nodesetWarnings.Add(new WarningsByNodeSet()
                                        { ProfileId = profileAndNodeSet.Profile.ID.Value, Key = profileAndNodeSet.Profile.ToString(), Warnings = logList });
                                    }
                                }
                                if (upgradePreviousVersions)
                                {
                                    await UpgradeToProfileAsync(profileAndNodeSet.Profile, userToken);
                                }
                                profileAndNodeSet.NodeSetModel.NewInThisImport = false;
                                break;
                            }
                        }
                    } while (profilesAndNodeSets.Any(pn => pn.NodeSetModel.NewInThisImport));
                    sw.Stop();
                    var elapsed = sw.Elapsed;
                    var elapsedMsg = $"{elapsed.Minutes}:{elapsed.Seconds} (min:sec)";
                    _logger.LogTrace($"Timestamp||ImportId:{logId}||Import time: {elapsedMsg}, Nodesets: {fileNames} "); //use warning so it shows in app log in db

                    //return success message object
                    filesImportedMsg = $"Imported Nodeset{(nodeSetXmlList.Count.Equals(1) ? "" : "s")}: {fileNames}";
                    await logToImportLog($"{filesImportedMsg}", TaskStatusEnum.Completed);
                }
                catch (Exception e)
                {
                    sw.Stop();
                    var elapsed2 = sw.Elapsed;
                    var elapsedMsg2 = $"{elapsed2.Minutes}:{elapsed2.Seconds} (min:sec)";
                    _logger.LogWarning($"Timestamp||ImportId:{logId}||Import time before failure: {elapsedMsg2}, Files: {fileNames} "); //use warning so it shows in app log in db
                                                                                                                                        //log complete message to logger and abbreviated message to user. 
                    _logger.LogCritical(e, $"ImportId:{logId}||ImportService|ImportOpcUaProfile|{e.Message}");
                    //TBD - once we stabilize, take out the specific exception message returned to user because user should not see a code message.
                    _profileDal.RollbackTransaction();
                    var message = e.InnerException != null ? e.InnerException.Message : e.Message;
                    // TODO is it necessary to reacquire the import log dal?
                    await logToImportLog($"An error occurred during the import: {message}.<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                    //await CreateImportLogMessage(GetImportLogDalIsolated(), logId, userToken, $"An error occurred during the import: {message}.<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"ImportId:{logId}||ImportOpcUaNodeSet error", ex);
                _profileDal.RollbackTransaction();
                // TODO is it necessary to reaquire the import log dal?
                await logToImportLog($"An error occurred during the import: {ex.Message}.<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
                //await CreateImportLogMessage(GetImportLogDalIsolated(), logId, userToken, $"An error occurred during the import: {ex.Message}.<br/>{filesImportedMsg}", TaskStatusEnum.Failed);
            }

            return nodesetWarnings;
        }

        private Task UpgradeToProfileAsync(ProfileModel profile, UserToken userToken)
        {
            return (_dal as ProfileTypeDefinitionDAL).UpgradeToProfileAsync(profile, _ptAnalyticsRepo, _ptFavoritesRepo, _dtRankRepo);
        }

        private ProfileModel FindOrCreateProfileForNodeSet(ModelValue tModel, string cloudLibId, IDal<Profile, ProfileModel> dalProfile, UserToken userToken, int logId, Stopwatch sw, bool allowMultiVersion)
        {
            var nsModel = tModel.NameVersion.CCacheId as NodeSetFileModel;
            _logger.LogTrace($"Timestamp||ImportId:{logId}||Loading nodeset {tModel.NameVersion.ModelUri}: {sw.Elapsed}");
            var profiles = dalProfile.Where(p => p.Namespace == tModel.NameVersion.ModelUri && p.PublishDate == tModel.NameVersion.PublicationDate && p.Version == tModel.NameVersion.ModelVersion /*&& (p.AuthorId == null || p.AuthorId == userToken)*/,
                    userToken, verbose: false)?.Data?.OrderByDescending(p => p.Version);
            var profile = profiles?.FirstOrDefault();
            _logger.LogTrace($"Timestamp||ImportId:{logId}||Loaded nodeset {tModel.NameVersion.ModelUri}: {sw.Elapsed}");

            if (profile == null)
            {
                var otherProfileVersion = dalProfile.Where(p => p.Namespace == tModel.NameVersion.ModelUri,
                    userToken, verbose: false)?.Data?.OrderByDescending(p => p.Version).FirstOrDefault();
                if (otherProfileVersion != null && !allowMultiVersion)
                {
                    throw new Exception($"Profile {tModel.NameVersion.ModelUri} already has version {otherProfileVersion.Version} {otherProfileVersion.PublishDate}. Can not import {tModel.NameVersion.ModelVersion} {tModel.NameVersion.PublicationDate}");
                }
                CloudLibProfileModel cloudLibNodeSet = null;
                if (cloudLibId != null)
                {
                    cloudLibNodeSet = _cloudLibDal.GetById(cloudLibId).Result;
                }
                if (cloudLibNodeSet == null)
                {
                    cloudLibNodeSet = _cloudLibDal.GetAsync(tModel.NameVersion.ModelUri, tModel.NameVersion.PublicationDate, true).Result;
                    if (cloudLibNodeSet == null)
                    {
                        // No exact match: use a newer one to prevent editing of older nodesets
                        cloudLibNodeSet = _cloudLibDal.GetAsync(tModel.NameVersion.ModelUri, tModel.NameVersion.PublicationDate, false).Result;
                        if (cloudLibNodeSet != null)
                        {
                            _logger.LogWarning($"Did not find exact match for {tModel.NameVersion}. Using standard nodeset with publication date {cloudLibNodeSet?.PublishDate} instead.");
                        }
                        else
                        {
                            _logger.LogWarning($"Did not find newer version for {tModel.NameVersion}. Not treating as standard node set to allow editing of future versions.");
                        }
                    }
                }

                if (cloudLibNodeSet != null)
                {
                    // Use cloud library meta data even if not exact match
                    profile = cloudLibNodeSet;
                }
                else
                {
                    profile = new ProfileModel();
                }
                profile.Namespace = tModel.NameVersion.ModelUri;
                profile.PublishDate = tModel.NameVersion.PublicationDate;
                profile.Version = tModel.NameVersion.ModelVersion;
                var xmlSchemaUri = tModel.NodeSet?.Models?.FirstOrDefault()?.XmlSchemaUri;
                if (!string.IsNullOrEmpty(xmlSchemaUri))
                {
                    profile.XmlSchemaUri = xmlSchemaUri;
                }
                profile.AuthorId = nsModel.AuthorId;
            }

            if (profile.NodeSetFiles == null)
            {
                profile.NodeSetFiles = new List<NodeSetFileModel>();
            }
            if (!profile.NodeSetFiles.Any(m => m.FileName == nsModel.FileName && m.PublicationDate == nsModel.PublicationDate && m.Version == nsModel.Version))
            {
                profile.NodeSetFiles.Add(nsModel);
            }
            return profile;
        }

        private UANodeSetImportResult CacheAndDownloadNodeSetFiles(UANodeSetDBCache myNodeSetCache, UserToken userToken, List<string> nodeSetXmlStringList, Func<string, TaskStatusEnum, Task> logToImportLog)
        {
            OnNodeSet callback = (string namespaceUri, DateTime? publicationDate) =>
            {
                logToImportLog($"Downloading from Cloud Library: {namespaceUri} {publicationDate}", TaskStatusEnum.InProgress).Wait();
            };
            UANodeSetImportResult resultSet;
            try
            {
                _cloudLibResolver.OnDownloadNodeSet += callback;
                var cacheManager = new UANodeSetCacheManager(myNodeSetCache, _cloudLibResolver);
                resultSet = cacheManager.ImportNodeSets(nodeSetXmlStringList, false, userToken);
            }
            finally
            {
                _cloudLibResolver.OnDownloadNodeSet -= callback;
            }

            return resultSet;
        }


        public System.Threading.Tasks.Task<List<NodeSetModel>> LoadNodeSetAsync(IOpcUaContext opcContext, UANodeSet nodeSet, ProfileModel profile, bool doNotReimport = false)
        {
            if (!nodeSet.Models.Any())
            {
                var ex = new Exception($"Invalid nodeset: no models specified");
                Logger.LogError(ex.Message);
                throw ex;
            }

            if (nodeSet.Models.Length > 1)
            {
                var ex = new Exception($"Nodeset: multiple model entries not supported.");
                Logger.LogError(ex.Message);
                throw ex;
            }

            var firstModel = nodeSet.Models.FirstOrDefault();
            if (firstModel.ModelUri != profile.Namespace)
            {
                throw new Exception($"Mismatching primary model meta data and meta data from cache");
            }
            if (
                firstModel.Version != profile.Version
                || ((firstModel.PublicationDateSpecified ? firstModel.PublicationDate.ToUniversalTime() : null) != profile.PublishDate?.ToUniversalTime())
                )
            {
                var message = $"Warning: Newer version {firstModel.Version} of {firstModel.ModelUri} required. Profile designer only offers {profile.Version}. Attempting to load anyway.";
                opcContext.Logger.LogWarning(message);
                //throw new Exception(message);
            }

            return NodeModelFactoryOpc.LoadNodeSetAsync(opcContext, nodeSet, profile, this.Aliases, doNotReimport);
        }

        public static readonly ImmutableList<string> _coreNodeSetUris = ImmutableList<string>.Empty.AddRange(new[] { strOpcNamespaceUri, strOpcDiNamespaceUri });

        public async System.Threading.Tasks.Task<Dictionary<string, ProfileTypeDefinitionModel>> ImportNodeSetModelAsync(NodeSetModel nodeSetModel, IDALContext dalContext, UserToken userToken)
        {
#if NODESETDBTEST
            {
                var sw2 = Stopwatch.StartNew();
                Logger.LogTrace($"Saving NodeSetModel");
                nsDBContext.SaveChanges();
                Logger.LogTrace($"Saved NodeSetModel after {sw2.Elapsed}");

                var savedModel = nsDBContext.NodeSets
                    .Where(m => m.ModelUri == nodeSetModel.ModelUri && m.PublicationDate == nodeSetModel.PublicationDate)
                    .FirstOrDefault();
                //.ToList();
                //var savedModel2 = nsDBContext.NodeSets.Find(nodeSetModel.ModelUri, nodeSetModel.PublicationDate);
            }
#endif

            ProfileModel profile = (ProfileModel)nodeSetModel.CustomState;

            var authorToken = userToken;
            if (_coreNodeSetUris.Contains(profile.Namespace))
            {
                userToken = UserToken.GetGlobalUser(userToken);
                authorToken = null;
            }

            dalContext.SetUser(userToken, authorToken);

            _dal.StartTransaction();
            foreach (var nsFile in profile.NodeSetFiles)
            {
                await _nodeSetFileDal.UpsertAsync(nsFile, userToken, true);
            }
            await _profileDal.UpsertAsync(profile, userToken, true);
            var profileItemsByNodeId = ImportProfileItems(nodeSetModel, dalContext);

            // Update XmlSchemaUri in case it was not specified in the UANodeset's Model, but found in a (legacy) DataTypeDictionaryType
            if (profile.XmlSchemaUri == null && nodeSetModel.XmlSchemaUri != null)
            {
                profile.XmlSchemaUri = nodeSetModel.XmlSchemaUri;
                await _profileDal.UpsertAsync(profile, userToken, true);
            }

            var sw = Stopwatch.StartNew();
            Logger.LogTrace($"Commiting transaction");
            await _dal.CommitTransactionAsync();
            Logger.LogTrace($"Committed transaction after {sw.Elapsed}");


            if ((profile.ID ?? 0) == 0)
            {
                // Ensure that the Profile has an ID, as it is referenced by the imported NodeModels.
                var writtenProfile = await _profileDal.GetExistingAsync(profile, userToken);
                profile.ID = writtenProfile?.ID;
            }
            return profileItemsByNodeId;
        }

        public List<(UANodeSet nodeSet, string xml, NodeSetModel model, Dictionary<string, NodeSetModel> requiredModels)> ExportNodeSet(CESMII.ProfileDesigner.DAL.Models.ProfileModel nodeSetModel, UserToken userToken, UserToken authorId, bool includeRequiredModels, bool bForceReexport)
        {
            List<(UANodeSet nodeSet, string xml, NodeSetModel model, Dictionary<string, NodeSetModel> requiredModels)> exportedNodeSets = new();
            var exportedNodeSet = ExportInternal(nodeSetModel, userToken, authorId, bForceReexport);
            exportedNodeSets.Add(exportedNodeSet);
            if (includeRequiredModels)
            {
                var requiredModels = exportedNodeSet.nodeSet.Models.SelectMany(m => m.RequiredModel)?.GroupBy(m => m.ModelUri).Select(mg => mg.MaxBy(m => m.GetNormalizedPublicationDate())).ToList();
                foreach (var requiredModel in requiredModels)
                {
                    var requiredProfile = _profileDal.Where(p => p.Namespace == requiredModel.ModelUri && p.PublishDate >= requiredModel.GetNormalizedPublicationDate(), userToken).Data?.OrderByDescending(p => p.PublishDate)?.FirstOrDefault();
                    var requiredNodeSet = ExportInternal(requiredProfile, userToken, authorId, bForceReexport);
                    exportedNodeSets.Add(requiredNodeSet);
                }
            }
            return exportedNodeSets;
        }

        public (UANodeSet nodeSet, string xml, NodeSetModel model, Dictionary<string, NodeSetModel> requiredModels) ExportInternal(ProfileModel profileModel, UserToken userToken, UserToken authorId, bool bForceReexport)
        {
            if (!string.IsNullOrEmpty(profileModel.CloudLibraryId) && !bForceReexport)
            {
                // Use the original XML for profiles imported from the cloud library (if available)
                var nodeSetFile = _nodeSetFileDal.Where(nsf => nsf.Profiles.Any(p => p.ID == profileModel.ID), userToken, verbose: true).Data?.FirstOrDefault();
                var nodeSetXml = nodeSetFile?.FileCache;
                if (nodeSetXml != null)
                {
                    using (MemoryStream ms = new(Encoding.UTF8.GetBytes(nodeSetXml)))
                    {
                        var nodeSet = UANodeSet.Read(ms);
                        return (nodeSet, nodeSetXml, null, null);
                    }
                }
            }
            Dictionary<string, NodeSetModel> nodeSetModels = new();
            var dalOpcContext = new DalOpcContext(this, nodeSetModels, userToken, authorId, false);

            // pre-load OPC UA base model
            if (!nodeSetModels.ContainsKey(strOpcNamespaceUri))
            {
                try
                {
                    // TODO find the right OPC version references in the nodeSet?
                    // For now: take the highest available version (special core nodeset versioning logic)
                    var opcNodeSetModel = _profileDal.Where(ns => ns.Namespace == strOpcNamespaceUri, userToken, null, null, false, true).Data
                        .OrderByDescending(m => string.Join(".", m.Version.Split(".").Take(2))) // First by version family
                        .ThenByDescending(m => m.PublishDate) //Then by publishdate, within the version family
                        .FirstOrDefault(); // Order by version family

                    // workaround for bug https://github.com/dotnet/runtime/issues/67622
                    var fileCachePatched = opcNodeSetModel.NodeSetFiles[0].FileCache.Replace("<Value/>", "<Value xsi:nil='true' />");
                    using (MemoryStream nodeSetStream = new MemoryStream(Encoding.UTF8.GetBytes(fileCachePatched)))
                    {
                        UANodeSet nodeSet = UANodeSet.Read(nodeSetStream);

                        var opcModel = nodeSet.Models[0];
                        var opcProfile = _profileDal.Where(ns => ns.Namespace == opcModel.ModelUri, userToken, null, null).Data?.FirstOrDefault();

                        if (profileModel.Namespace != strOpcNamespaceUri)
                        {
                            //TODO - this next line is time consuming, but still faster than loading from database.
                            this.LoadNodeSetAsync(dalOpcContext, nodeSet, opcProfile, true).Wait();
                        }
                        else
                        {
                            // For core nodeset testing, populate the aliases (for easier diffing) but load the actual nodes from profile database
                            foreach (var alias in nodeSet.Aliases)
                            {
                                this.Aliases.TryAdd(alias.Value, alias.Alias);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, "Internal error preparing for nodeset export.");
                }
            }

            // populate the model being exported with proper version and publication date
            dalOpcContext.GetOrAddNodesetModel(
                new ModelTableEntry
                {
                    ModelUri = profileModel.Namespace,
                    PublicationDate = profileModel.PublishDate ?? default,
                    Version = profileModel.Version,
                    PublicationDateSpecified = profileModel.PublishDate != null,
                    XmlSchemaUri = profileModel.XmlSchemaUri,
                }, true);

            var profileItemsResult = _dal.Where(pi => pi.ProfileId == profileModel.ID /*&& (pi.AuthorId == null || pi.AuthorId == userId)*/, userToken, null, null, false, true);
            if (profileItemsResult.Data != null)
            {
                foreach (var profile in profileItemsResult.Data)
                {
                    NodeModelFromProfileFactory.Create(profile, dalOpcContext, dalOpcContext);
                }
            }

            // Export the nodesets
            UANodeSet exportedNodeSet = null;
            var modelsToExport = nodeSetModels.Values.Where(model =>
                ((ProfileModel)model.CustomState).Namespace == profileModel.Namespace
                && ((ProfileModel)model.CustomState).PublishDate == profileModel.PublishDate).ToList();
            if (modelsToExport.Count != 1)
            {
                this.Logger.LogWarning($"Not exactly one model to export: {string.Join(", ", modelsToExport.Select(m => m.ToString()))}");
            }
            foreach (var model in modelsToExport)
            {
#if NODESETDBTEST
                model.AllNodesByNodeId.Clear();
                nsDBContext.Set<NodeModel>().Where(nm => nm.NodeSet == model).ForEachAsync(nm => model.AllNodesByNodeId.Add(nm.NodeId, nm)).Wait();
                
                foreach (var required in model.RequiredModels)
                {
                    var requiredNodeSet = GetOrAddNodesetModel(new ModelTableEntry { ModelUri = required.ModelUri, PublicationDate = required.PublicationDate ?? default, PublicationDateSpecified = required.PublicationDate != null });
                    requiredNodeSet.AllNodesByNodeId.Clear();
                    nsDBContext.Set<NodeModel>().Where(nm => nm.NodeSet == requiredNodeSet).ForEachAsync(nm => requiredNodeSet.AllNodesByNodeId.Add(nm.NodeId, nm)).Wait();
                    
                }
#else
                model.UpdateIndices();
#endif
                exportedNodeSet = UANodeSetModelExporter.ExportNodeSet(model, nodeSetModels, this._logger,this.Aliases);
            }
            // .Net6 changed the default to no-identation: https://github.com/dotnet/runtime/issues/64885
            string exportedNodeSetXml;
            using (MemoryStream ms = new())
            {
                using (StreamWriter writer = new(ms, new UTF8Encoding(false)))
                {
                    try
                    {
                        var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true, });
                        XmlSerializer serializer = new XmlSerializer(typeof(UANodeSet));
                        serializer.Serialize(xmlWriter, exportedNodeSet);
                    }
                    finally
                    {
                        writer.Flush();
                    }
                }
                exportedNodeSetXml = Encoding.UTF8.GetString(ms.ToArray());
            }
            return (exportedNodeSet, exportedNodeSetXml, modelsToExport.FirstOrDefault(), nodeSetModels);
        }

        /// <summary>
        /// Creates Profile Items in the backend store based for all OPC nodes in the nodeset model
        /// </summary>
        /// <param name="nodesetModel">nodeset model to import</param>
        /// <param name="updateExisting">Indicates if existing profile items should be updated/overwritten or kept unchanged.</param>
        /// <returns></returns>
        private static Dictionary<string, ProfileTypeDefinitionModel> ImportProfileItems(NodeSetModel nodesetModel, IDALContext dalContext)
        {
            nodesetModel.UpdateIndices();
            foreach (var dataType in nodesetModel.DataTypes)
            {
                dataType.ImportProfileItem(dalContext);
            }
            foreach (var customType in nodesetModel.VariableTypes)
            {
                customType.ImportProfileItem(dalContext);
            }
            foreach (var uaInterface in nodesetModel.Interfaces)
            {
                uaInterface.ImportProfileItem(dalContext);
            }
            foreach (var objectType in nodesetModel.ObjectTypes)
            {
                objectType.ImportProfileItem(dalContext);
            }

            foreach (var uaObject in nodesetModel.Objects)
            {
                uaObject.ImportProfileItem(dalContext);
            }

            foreach (var uaVariable in nodesetModel.DataVariables)
            {
                if (dalContext.profileItemsByNodeId.TryGetValue(uaVariable.NodeId, out _))
                {
                    continue;
                }
                {
                    // Ignore type system information
                    bool bIsTypeSystem = false;
                    var dvParent = uaVariable.Parent;
                    while (dvParent != null)
                    {
                        if (dvParent?.NodeId == $"nsu=http://opcfoundation.org/UA/;{ObjectIds.XmlSchema_TypeSystem}" || dvParent?.NodeId == $"nsu=http://opcfoundation.org/UA/;{ObjectIds.OPCBinarySchema_TypeSystem}")
                        {
                            dalContext.Logger.LogInformation($"UAVariable {uaVariable} ({uaVariable.GetDisplayNamePath()}) ignored because it is part of the global OPC UA type system node and makes little sense in a nodeset.");
                            bIsTypeSystem = true;
                            break;
                        }
                        dvParent = (dvParent as InstanceModelBase)?.Parent;
                    };
                    if (bIsTypeSystem)
                    {
                        continue;
                    }
                }

                if (uaVariable.Parent is DataVariableModel)
                {
                    var variableModel = uaVariable;
                    var parentModel = uaVariable.Parent;
                    //do
                    //{
                    if (dalContext.profileItemsByNodeId.TryGetValue(parentModel.NodeId, out var parentProfileItem))
                    {
                        var nodeIdParts = variableModel.NodeId.Split(';');
                        if (parentProfileItem.Attributes.FirstOrDefault(a => a.OpcNodeId == nodeIdParts[1] && nodeIdParts[0].EndsWith(a.Namespace)) != null)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (uaVariable.Parent is DataVariableModel dvParentModel && dvParentModel.Parent != null && dalContext.profileItemsByNodeId.TryGetValue(dvParentModel.Parent.NodeId, out var dvGrandParent))
                        {

                            var nodeIdParts = uaVariable.Parent.NodeId.Split(';');
                            var attribute = dvGrandParent.Attributes?.FirstOrDefault(a => a.OpcNodeId == nodeIdParts[1] && nodeIdParts[0].EndsWith(a.Namespace));
                            if (attribute != null && !string.IsNullOrEmpty(attribute.DataVariableNodeIds))
                            {
                                var map = DataVariableNodeIdMap.GetMap(attribute.DataVariableNodeIds);
                                if (map?.DataVariableNodeIdsByBrowseName.ContainsKey(uaVariable.BrowseName) == true)
                                {
                                    continue;
                                }
                            }
                        }
                    }
                    //} while (false); // containingNode != null); // TODO support nested datavariable
                }
                if (uaVariable.Parent is ObjectTypeModel || uaVariable.Parent is ObjectModel || uaVariable.Parent is VariableTypeModel)
                {
                    if (dalContext.profileItemsByNodeId.TryGetValue(uaVariable.Parent.NodeId, out var parent))
                    {
                        var nodeIdParts = uaVariable.NodeId.Split(';');
                        if (parent.Attributes?.FirstOrDefault(a => a.OpcNodeId == nodeIdParts[1] && nodeIdParts[0].EndsWith(a.Namespace)) != null)
                        {
                            continue;
                        }
                    }
                }
                bool bFound = false;
                foreach (var referencingNode in uaVariable.OtherReferencingNodes)
                {
                    if (dalContext.profileItemsByNodeId.TryGetValue(referencingNode.Node.NodeId, out var referencingProfileType))
                    {
                        var nodeIdParts = uaVariable.NodeId.Split(';');
                        if (referencingProfileType.Attributes?.FirstOrDefault(a => a.OpcNodeId == nodeIdParts[1] && nodeIdParts[0].EndsWith(a.Namespace)) != null)
                        {
                            bFound = true;
                            break;
                        }
                    }
                }
                if (bFound)
                {
                    continue;
                }

                if (uaVariable.Parent != null && uaVariable.Parent.Namespace != uaVariable.Namespace)
                {
                    dalContext.Logger.LogWarning($"UAVariable {uaVariable} ({uaVariable.GetDisplayNamePath()}) is parented in {uaVariable.Parent} in a different namespace and may be ignored.");
                    continue;
                }

                dalContext.Logger.LogWarning($"UAVariable {uaVariable} ({uaVariable.GetDisplayNamePath()}) ignored.");
            }

            return dalContext.profileItemsByNodeId;
        }

        public async Task ImportEngineeringUnitsAsync(UserToken userToken)
        {
            var units = NodeModelOpcExtensions.UNECEEngineeringUnits;//.GetUNECEEngineeringUnits();
            _euDal.StartTransaction();
            foreach (var unit in units)
            {
                await _euDal.AddAsync(new EngineeringUnitModel
                {
                    DisplayName = unit.DisplayName.Text,
                    Description = unit.Description.Text,
                    NamespaceUri = unit.NamespaceUri,
                    UnitId = unit.UnitId,
                }, userToken);
            }
            await _euDal.CommitTransactionAsync();
        }

        private sealed class ProfileModelAndNodeSet
        {
            public ProfileModel Profile { get; set; }
            public ModelValue NodeSetModel { get; set; }
        }

        public sealed class WarningsByNodeSet
        {
            public int ProfileId { get; set; }
            public string Key { get; set; }
            public List<string> Warnings { get; set; }
        }

    }

    public class LoggerCapture : ILogger<OpcUaImporter>
    {
        private readonly ILogger<OpcUaImporter> logger;

        public LoggerCapture(ILogger<OpcUaImporter> logger)
        {
            this.logger = logger;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                LogList?.Add($"{logLevel}: {formatter(state, exception)}");
            }
            logger.Log(logLevel, eventId, state, exception, formatter);
        }
        public List<string> LogList { get; set; }
    }
}