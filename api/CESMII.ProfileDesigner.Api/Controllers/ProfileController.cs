using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Linq.Expressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Opc.Ua.Cloud.Library.Client;

using CESMII.ProfileDesigner.OpcUa.AASX;
using CESMII.ProfileDesigner.Api.Shared.Controllers;
using CESMII.ProfileDesigner.Api.Shared.Models;
using CESMII.ProfileDesigner.Common;
using CESMII.ProfileDesigner.Common.Enums;
using CESMII.ProfileDesigner.DAL;
using CESMII.ProfileDesigner.DAL.Models;
using CESMII.ProfileDesigner.Data.Entities;
using CESMII.ProfileDesigner.Data.Extensions;
using CESMII.ProfileDesigner.OpcUa;
using CESMII.ProfileDesigner.Api.Utils;

using CESMII.OpcUa.NodeSetImporter;
using CESMII.OpcUa.NodeSetModel.Factory.Smip;
using CESMII.Common.CloudLibClient;

namespace CESMII.ProfileDesigner.Api.Controllers
{
    [Route("api/[controller]")]
    [Authorize(Policy = nameof(PermissionEnum.UserAzureADMapped))]
    public class ProfileController : BaseController<ProfileController>
    {
        private readonly IDal<Profile, ProfileModel> _dal;
        private readonly ProfileTypeDefinitionDAL _dalProfileType;
        private readonly ICloudLibDal<CloudLibProfileModel> _cloudLibDal;
        private readonly CloudLibraryUtil _cloudLibUtil;

        private readonly Utils.ImportService _svcImport;
        private readonly OpcUaImporter _exporter;
        private readonly List<string> _permissibleLicenses = new() { "MIT", "BSD-3-Clause" };

        public ProfileController(IDal<Profile, ProfileModel> dal,
            ProfileTypeDefinitionDAL dalProfileType,
            ICloudLibDal<CloudLibProfileModel> cloudLibDal,
            UserDAL dalUser,
            Utils.ImportService svcImport,
            OpcUaImporter exporter,
            ConfigUtil config, ILogger<ProfileController> logger,
            CloudLibraryUtil cloudLibUtil)
            : base(config, logger, dalUser)
        {
            _dal = dal;
            _dalProfileType = dalProfileType;
            _cloudLibDal = cloudLibDal;
            _svcImport = svcImport;
            _exporter = exporter;
            _cloudLibUtil = cloudLibUtil;

            logger.LogInformation($"ProfileController created!");
        }

        [HttpPost, Route("GetByID")]
        //[ProducesResponseType(200, Type = typeof(NodeSetModel))]
        [ProducesResponseType(200, Type = typeof(ProfileTypeDefinitionModel))]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetByID([FromBody] IdIntModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|GetByID|Invalid model (null)");
                return BadRequest($"Invalid model (null)");
            }

            var result = _dal.GetById(model.ID, base.DalUserToken);
            if (result == null)
            {
                _logger.LogWarning($"ProfileController|GetById|No records found matching this ID: {model.ID}");
                return BadRequest($"No records found matching this ID: {model.ID}");
            }

            //query cloud lib to get the approval status of this item.
            var containerTemp = new List<ProfileModel>() { result };
            await UpdateCloudLibraryStatus(containerTemp, false);

            return Ok(result);
        }

        /// <summary>
        /// User can look up profile by passing in Cloud library Id. This would happen when marketplace
        /// user wants to inspect profile at a more granular level and view in profile designer. 
        /// </summary>
        /// <remarks>The user may or may not have downloaded the profile prior to this request. 
        /// If the profile is not present, return null to caller. The caller will then trigger
        /// a separate call to import the profile.  
        /// </remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("GetByCloudLibId")]
        [ProducesResponseType(200, Type = typeof(ProfileModel))]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetByCloudLibId([FromBody] IdStringModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|GetByCloudLibId|Invalid model (null)");
                return BadRequest($"Invalid model (null)");
            }

            var result = _dal.Where(x => x.CloudLibraryId.Equals(model.ID),
                base.DalUserToken, null, null, false, true).Data;
            if (result == null || result.Count == 0)
            {
                return Ok(null);
            }

            //query cloud lib to get the approval status of this item.
            await UpdateCloudLibraryStatus(result, true);

            return Ok(result.FirstOrDefault());
        }

        /// <summary>
        /// Search my profiles library for profiles matching criteria passed in. This is a simple search field and 
        /// this will check against several profile fields and return results. 
        /// </summary>
        /// <remarks>Items in my profiles will have an author id</remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("Mine")]
        [ProducesResponseType(200, Type = typeof(DALResult<ProfileModel>))]
        public IActionResult GetMine([FromBody] PagerFilterSimpleModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|GetMine|Invalid model.");
                return BadRequest("Profile|GetMine|Invalid model");
            }

            if (string.IsNullOrEmpty(model.Query))
            {
                return Ok(_dal.Where(s => string.IsNullOrEmpty(s.CloudLibraryId) /*&& s.AuthorId.HasValue && s.AuthorId.Value.Equals(userId)*/,
                                base.DalUserToken, model.Skip, model.Take, true));
            }

            model.Query = model.Query.ToLower();
            var result = _dal.Where(s =>
                            string.IsNullOrEmpty(s.CloudLibraryId) && /*s.AuthorId.HasValue && s.AuthorId.Value.Equals(userId) &&*/
                            //string query section
                            (s.Namespace.ToLower().Contains(model.Query)
                            //|| (s.Author != null && (s.Author.FirstName.ToLower() + s.Author.LastName.ToLower()).Contains(
                            //    model.Query.Replace(" ", "").Replace("-", ""))) ||  //in case they search for code and name in one string.
                            ),
                            base.DalUserToken, model.Skip, model.Take, true);
            return Ok(result);
        }

        /// <summary>
        /// Search profiles library for profiles matching criteria passed in. This is a simple search field and 
        /// this will check against several profile fields and return results. 
        /// </summary>
        /// <remarks>Items in profiles library will not have an author id</remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("library")]
        [ProducesResponseType(200, Type = typeof(DALResult<ProfileModel>))]
        public async Task<IActionResult> GetLibraryAsync([FromBody] ProfileTypeDefFilterModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|GetLibrary|Invalid model");
                return BadRequest("Profile|Library|Invalid model");
            }

            return Ok(await SearchProfilesAsync(model));
        }

        #region Search Helper Functions
        /// <summary>
        /// Common search code for searching profiles when given some set of search criteria
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        private async Task<DALResult<ProfileModel>> SearchProfilesAsync([FromBody] ProfileTypeDefFilterModel model)
        {
            //search on some pre-determined fields
            var orderByExprs = BuildSearchOrderByExpressions(LocalUser.ID.Value, model.SortByEnum);
            var result = _dal.Where(BuildPredicate(model, base.DalUserToken), base.DalUserToken, model.Skip, model.Take, true, false, orderByExprs.ToArray());

            await UpdateCloudLibraryStatus(result?.Data, true);  //refresh all in case cloud library changed status of prev approved outside of Profile Designer

            return new DALResult<ProfileModel>()
            {
                Count = result.Count,
                Data = result.Data,
                SummaryData = result.SummaryData
            };
        }

        private async Task UpdateCloudLibraryStatus(List<ProfileModel> profiles, bool refreshAll)
        {
            if (refreshAll || profiles?.Any(p => p.CloudLibPendingApproval == true) == true)
            {
                List<CloudLibProfileModel> pendingNodesets = new();
                try
                {
                    string cursor = null;
                    do
                    {
                        var cloudResultPage = await _cloudLibDal.GetNodeSetsPendingApprovalAsync(100, cursor, false, additionalProperty: new AdditionalProperty { Name = ICloudLibDal<CloudLibProfileModel>.strCESMIIUserInfo, Value = $"PD{base.DalUserToken.UserId}" });
                        pendingNodesets.AddRange(cloudResultPage.Nodes);
                        cursor = cloudResultPage.PageInfo.HasNextPage ? cloudResultPage.PageInfo.EndCursor : null;
                    }
                    while (cursor != null);
                    if (pendingNodesets?.Any() == true)
                    {
                        foreach (var clProfile in pendingNodesets)
                        {
                            var profile = profiles.FirstOrDefault(p => p.CloudLibraryId == clProfile.CloudLibraryId);
                            if (profile != null)
                            {
                                profile.CloudLibApprovalStatus = clProfile.CloudLibApprovalStatus;
                                profile.CloudLibApprovalDescription = clProfile.CloudLibApprovalDescription;
                                if (profile.CloudLibPendingApproval == false)
                                {
                                    profile.CloudLibPendingApproval = true;
                                    await _dal.UpdateAsync(profile, base.DalUserToken);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving cloud library approval status");
                    pendingNodesets = null;
                }
                try
                {
                    foreach (var profile in profiles.Where(p => p.CloudLibPendingApproval == true))
                    {
                        if (pendingNodesets == null)
                        {
                            profile.CloudLibApprovalStatus = "UNKNOWN";
                        }
                        else if (!pendingNodesets.Any(p => p.CloudLibraryId == profile.CloudLibraryId))
                        {
                            try
                            {
                                var cloudLibProfile = await _cloudLibDal.GetById(profile.CloudLibraryId);
                                if (cloudLibProfile == null)
                                {
                                    profile.CloudLibPendingApproval = false;
                                    profile.CloudLibraryId = null;
                                    await _dal.UpdateAsync(profile, base.DalUserToken);
                                }
                                else
                                {
                                    profile.CloudLibApprovalStatus = cloudLibProfile.CloudLibApprovalStatus;
                                    profile.CloudLibApprovalDescription = cloudLibProfile.CloudLibApprovalDescription;
                                    if (cloudLibProfile.CloudLibApprovalStatus == "APPROVED")
                                    {
                                        profile.CloudLibPendingApproval = false;
                                        await _dal.UpdateAsync(profile, base.DalUserToken);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error retrieving cloud library approval status for {profile}");
                                profile.CloudLibApprovalStatus = "UNKNOWN";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Internal Error updating cloud library approval status");
                }
            }
        }

        private List<OrderByExpression<Profile>> BuildSearchOrderByExpressions(int userId, SearchCriteriaSortByEnum val = SearchCriteriaSortByEnum.Name)
        {
            //build list of order bys based on user selection of an enum, default is sort by name. 
            var result = new List<OrderByExpression<Profile>>();
            switch (val)
            {
                case SearchCriteriaSortByEnum.Author:
                    result.Add(new OrderByExpression<Profile>()
                    {
                        Expression = x => string.IsNullOrEmpty(x.CloudLibraryId) &&
                            x.AuthorId.HasValue && x.AuthorId.Equals(userId) ? 1 : 0,
                        IsDescending = true
                    });
                    result.Add(new OrderByExpression<Profile>()
                    {
                        Expression = x => (string.IsNullOrEmpty(x.Title) ?
                            x.Namespace.Replace("https://", "").Replace("http://", "") : x.Title).Trim()
                    });
                    result.Add(new OrderByExpression<Profile>() { Expression = x => x.Namespace.Replace("https://", "").Replace("http://", "").Trim() });
                    result.Add(new OrderByExpression<Profile>() { Expression = x => x.PublishDate, IsDescending = true });
                    break;
                //case SearchCriteriaSortByEnum.Popular:
                //case SearchCriteriaSortByEnum.Name:
                default:
                    result.Add(new OrderByExpression<Profile>()
                    {
                        Expression = x => (string.IsNullOrEmpty(x.Title) ?
                            x.Namespace.Replace("https://", "").Replace("http://", "") : x.Title).Trim()
                    });
                    result.Add(new OrderByExpression<Profile>() { Expression = x => x.Namespace.Replace("https://", "").Replace("http://", "").Trim() });
                    result.Add(new OrderByExpression<Profile>() { Expression = x => x.PublishDate, IsDescending = true });
                    break;
            }

            return result;
        }

        private List<Expression<Func<Profile, bool>>> BuildPredicate(ProfileTypeDefFilterModel model, UserToken userToken)
        {
            model.Query = string.IsNullOrEmpty(model.Query) ? null : model.Query.ToLower();

            //init 
            var result = new List<Expression<Func<Profile, bool>>>();

            //Part 0 - string contains
            if (!string.IsNullOrEmpty(model.Query))
            {
                result.Add(x => x.Namespace.ToLower().Contains(model.Query) ||
                         (x.Title != null && x.Title.ToLower().Contains(model.Query)) ||
                         (x.License != null && x.License.ToLower().Contains(model.Query)) ||
                         (x.Description != null && x.Description.ToLower().Contains(model.Query)) ||
                         (x.ContributorName != null && x.ContributorName.ToLower().Contains(model.Query)) ||
                         (x.Keywords != null && string.Join(",",x.Keywords).ToLower().Contains(model.Query)) ||
                         (x.CategoryName != null && x.CategoryName.ToLower().Contains(model.Query)) ||
                         (x.CopyrightText != null && x.CopyrightText.ToLower().Contains(model.Query)) ||
                         (x.Author != null && x.Author.DisplayName.ToLower().Contains(model.Query)));
            } 

            //Part 1 - Mine OR Base profiles - This will be an OR clause within this portion
            var filterProfileSource = model.Filters?.Find(c =>
                c.ID.Value == (int)ProfileSearchCriteriaCategoryEnum.Source)
                .Items.Where(x => x.Selected).ToList();
            if (filterProfileSource != null && filterProfileSource.Any())
            {
                Expression<Func<Profile, bool>> predSource = null;
                foreach (var f in filterProfileSource)
                {
                    if (f.ID == (int)ProfileSearchCriteriaSourceEnum.Mine)
                    {
                        Expression<Func<Profile, bool>> fnMine = x => string.IsNullOrEmpty(x.CloudLibraryId)
                            && x.AuthorId.Value.Equals(userToken.UserId);
                        predSource = predSource == null ? fnMine : predSource.OrExtension(fnMine);
                    }
                    else if (f.ID == (int)ProfileSearchCriteriaSourceEnum.BaseProfile)
                    {
                        Expression<Func<Profile, bool>> fnBase = x => !string.IsNullOrEmpty(x.CloudLibraryId);
                        predSource = predSource == null ? fnBase : predSource.OrExtension(fnBase);
                    }
                }
                //append to predicate list
                result.Add(predSource);
            }

            //Future filter scenarios can go here....

            return result;
        }
        #endregion

        /// <summary>
        /// Search profiles library for profiles matching criteria passed in. This is a simple search field and 
        /// this will check against several profile fields and return results. 
        /// </summary>
        /// <remarks>Items in profiles library will not have an author id</remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("cloudlibrary")]
        [ProducesResponseType(200, Type = typeof(DALResult<CloudLibProfileModel>))]
        public async Task<IActionResult> GetCloudLibrary([FromBody] CloudLibFilterModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|GetLibrary|Invalid model");
                return BadRequest("Profile|Library|Invalid model");
            }

            //convert query to list<string> needed by CloudLib
            var keywords = string.IsNullOrEmpty(model.Query) ? null : new List<string>() { model.Query.ToLower() };

            //get the include/exclude local filter - it is in Source group and it is defined by matching its id up to this enum
            //see lookupController where it is defined for the front end.
            bool localProfilesSelected = (model.Filters != null && model.Filters.Find(c =>
                c.ID.Value == (int)ProfileSearchCriteriaCategoryEnum.Source)
                .Items.Any(x => x.ID == (int)ProfileSearchCriteriaSourceEnum.BaseProfile && x.Selected));

            // Test hook, not usef in front end at this point
            bool myProfilesSelected = (model.Filters != null && model.Filters
                .Find(c =>
                    c.ID.Value == (int)ProfileSearchCriteriaCategoryEnum.Source)
                .Items.Any(
                    x => x.ID == 999 && x.Selected));


            // indicates if any local profiles should be removed from the profiles found in the cloud library
            // Use case: find new profiles that I don't already have
            bool excludeLocalLibrary = !localProfilesSelected;

            // indicates if any local profiles that are not also in the cloudlibrary should be added
            // Use case: browse around in all profiles that I have access to, local and cloud
            bool addLocalLibrary = myProfilesSelected;

            // Get both local and cloudlib cursors
            var cursors = model.Cursor?.Split(",");
            string cloudLibCursor = cursors?[0] ?? null;
            if (cloudLibCursor == string.Empty)
            {
                cloudLibCursor = null;
            }
            int localCursor;
            if (cursors?[1] != null)
            {
                localCursor = int.Parse(cursors[1]);
                if (model.PageBackwards)
                {
                    localCursor -= model.Take;
                    if (localCursor < 0)
                    {
                        localCursor = 0;
                    }
                }
            }
            else
            {
                localCursor = 0;
            }

            bool bFullResultCloud;
            bool bFullResultLocal = false;
            List<ProfileModel> allLocalProfiles = null;
            List<GraphQlNodeAndCursor<CloudLibProfileModel>> pendingCloudLibProfiles = new();
            List<ProfileModel> pendingLocalProfiles = null;

            int totalCount = 0;
            List<CloudLibProfileModel> result = new();
            string firstCursor = null;

            try
            {
                do
                {
                    // Get first batch of profiles from the cloudlib
                    var cloudResultTask = _cloudLibDal.Where(model.Take, cloudLibCursor, model.PageBackwards, keywords);

                    // Get local profiles in parallel
                    allLocalProfiles = allLocalProfiles ?? _dal.GetAll(base.DalUserToken);

                    // Wait for the cloudlib query to finish
                    var cloudResultPage = await cloudResultTask;
                    if (cloudResultPage.Edges.Count > model.Take)
                    {
                        _logger.LogWarning($"ProfileController|GetCloudLibrary|Received more profiles than requested: {result.Count}, expected {model.Take}.");
                    }
                    if (!model.PageBackwards || model.Cursor == null)
                    {
                        bFullResultCloud = !cloudResultPage.PageInfo.HasNextPage;
                    }
                    else
                    {
                        bFullResultCloud = !cloudResultPage.PageInfo.HasPreviousPage;
                    }
                    totalCount = cloudResultPage.TotalCount;
                    pendingCloudLibProfiles.AddRange(cloudResultPage.Edges);

                    if (excludeLocalLibrary)
                    {
                        // remove all profiles from the cloudlib result that are available locally: mark them as null for correct cursor handling of skipped profiles
                        foreach (var localProfile in allLocalProfiles)
                        {
                            var removedItemIndex = pendingCloudLibProfiles.FindLastIndex(p => p?.Node != null && p.Node.Namespace == localProfile.Namespace);
                            if (removedItemIndex >= 0)
                            {
                                pendingCloudLibProfiles[removedItemIndex].Node = null;
                                totalCount--;
                            }
                        }
                    }

                    if (addLocalLibrary)
                    {
                        if (pendingLocalProfiles == null)
                        {
                            pendingLocalProfiles = new();
                        }

                        // Query local profiles
                        if (keywords != null && keywords.Count > 0)
                        {
                            if (!bFullResultLocal)
                            {
                                var orderBy = new List<OrderByExpression<Profile>> {
                                        new OrderByExpression<Profile>
                                        {
                                            Expression = p => p.Namespace,
                                            IsDescending = false,
                                        },
                                        new OrderByExpression<Profile>
                                        {
                                            Expression = p => p.PublishDate,
                                            IsDescending = false,
                                        },
                                };
                                if (excludeLocalLibrary)
                                {
                                    // owned profiles first when excluding and adding
                                    orderBy.Insert(0, new OrderByExpression<Profile>
                                    {
                                        Expression = p => !p.AuthorId.HasValue || !string.IsNullOrEmpty(p.CloudLibraryId),
                                        IsDescending = false,
                                    });
                                }

                                string keywordRegex = ".*(" + string.Join(" | ", keywords) + ").*";

                                var newLocalProfilesResult = _dal.Where(
                                    new List<Expression<Func<Profile, bool>>>
                                    {
                                        p => Regex.IsMatch(String.IsNullOrEmpty(p.Namespace) ? "" : p.Namespace, keywordRegex, RegexOptions.IgnoreCase)
                                    },
                                    base.DalUserToken, localCursor, null, true,
                                    orderByExpressions: orderBy.ToArray());
                                var newLocalProfiles = newLocalProfilesResult.Data;
                                bFullResultLocal = newLocalProfiles.Count == model.Take || newLocalProfiles.Count == 0;
                                pendingLocalProfiles.AddRange(newLocalProfiles);
                                totalCount += newLocalProfilesResult.Count; // TODO this is not correct in all scenarios (local profiles that are also in the cloudlib)
                            }
                        }
                        else
                        {
                            bFullResultLocal = true;
                            pendingLocalProfiles.AddRange(
                                (excludeLocalLibrary ?
                                    allLocalProfiles.OrderBy(pm => pm.IsReadOnly).ThenBy(pm => pm.Namespace).ThenBy(pm => pm.PublishDate)// owned profiles first when excluding and adding
                                    : allLocalProfiles.OrderBy(pm => pm.Namespace).ThenBy(pm => pm.PublishDate)
                                )
                                .Skip(localCursor));
                            totalCount += allLocalProfiles.Count(p => string.IsNullOrEmpty(p.CloudLibraryId));
                        }
                    }
                    else
                    {
                        bFullResultLocal = true;
                    }
                    // Process from start of local or cloud lists until we have enough results
                    while (result.Count < model.Take
                        && (pendingLocalProfiles?.Any() == true || (pendingCloudLibProfiles.Any()))
                        && ((pendingLocalProfiles?.Any() == true || bFullResultLocal) || (pendingCloudLibProfiles.Any() || bFullResultCloud)))
                    {
                        var firstPendingCloudLibProfile = !model.PageBackwards ? pendingCloudLibProfiles.FirstOrDefault() : pendingCloudLibProfiles.LastOrDefault();
                        if (firstPendingCloudLibProfile != null && firstPendingCloudLibProfile.Node == null)
                        {
                            // Cloud profile was excluded earlier: skip it but remember it's cursor for the next query
                            if (!model.PageBackwards)
                            {
                                pendingCloudLibProfiles.RemoveAt(0);
                            }
                            else
                            {
                                pendingCloudLibProfiles.RemoveAt(pendingCloudLibProfiles.Count - 1);
                            }
                            cloudLibCursor = firstPendingCloudLibProfile.Cursor;
                            continue;
                        }

                        var firstPendingLocalprofile = !model.PageBackwards ? pendingLocalProfiles?.FirstOrDefault() : pendingLocalProfiles?.LastOrDefault();
                        if (firstPendingLocalprofile != null &&
                            (excludeLocalLibrary || // Put local library items first if both add and exclude is specified
                              (firstPendingCloudLibProfile?.Node == null || String.Compare(firstPendingLocalprofile.Namespace, firstPendingCloudLibProfile.Node.Namespace, false, CultureInfo.InvariantCulture) < 0)))
                        {
                            if (!result.Any())
                            {
                                firstCursor = $"{cloudLibCursor},{localCursor}";
                            }
                            if (!model.PageBackwards)
                            {
                                result.Add(CloudLibProfileModel.MapFromProfile(firstPendingLocalprofile));
                                pendingLocalProfiles.RemoveAt(0);
                                localCursor++;
                            }
                            else
                            {
                                result.Insert(0, CloudLibProfileModel.MapFromProfile(firstPendingLocalprofile));
                                pendingLocalProfiles.RemoveAt(pendingLocalProfiles.Count - 1);
                                localCursor--;
                            }
                        }
                        else
                        {
                            if (firstPendingCloudLibProfile != null)
                            {
                                cloudLibCursor = firstPendingCloudLibProfile.Cursor;
                                if (!result.Any())
                                {
                                    firstCursor = $"{cloudLibCursor},{localCursor}";
                                }
                                if (!model.PageBackwards)
                                {
                                    result.Add(firstPendingCloudLibProfile.Node);
                                    pendingCloudLibProfiles.RemoveAt(0);
                                }
                                else
                                {
                                    result.Insert(0, firstPendingCloudLibProfile.Node);
                                    pendingCloudLibProfiles.RemoveAt(pendingCloudLibProfiles.Count - 1);
                                }

                                if (firstPendingLocalprofile != null &&
                                    firstPendingLocalprofile.Namespace == firstPendingCloudLibProfile.Node.Namespace
                                    && firstPendingLocalprofile.PublishDate == firstPendingCloudLibProfile.Node.PublishDate)
                                {
                                    // Skip matching local profile to avoid duplicates
                                    if (!model.PageBackwards)
                                    {
                                        pendingLocalProfiles.RemoveAt(0);
                                        localCursor++;
                                    }
                                    else
                                    {
                                        pendingLocalProfiles.RemoveAt(pendingLocalProfiles.Count - 1);
                                        localCursor--;
                                    }
                                }
                            }
                        }
                    }
                } while ((!bFullResultCloud || !bFullResultLocal) && result.Count < model.Take);

            }
            catch (Exception ex)
            {
                _logger.LogError($"ProfileController|GetCloudLibrary|Exception: {ex.Message}.");
                return StatusCode(500, "Error processing query.");
            }

            // Fill in any local profile information for cloud library-only results
            foreach (var localProfile in allLocalProfiles ?? new List<ProfileModel>())
            {
                var cloudLibProfile = result.FirstOrDefault(p => p.Namespace == localProfile.Namespace);
                if (cloudLibProfile != null && cloudLibProfile.ID == null)
                {
                    // Add in local profile information
                    // TODO handle different publication dates between cloudlib and local library
                    cloudLibProfile.ID = localProfile.ID;
                    cloudLibProfile.AuthorId = localProfile.AuthorId;
                    cloudLibProfile.ImportWarnings = localProfile.ImportWarnings;
                    cloudLibProfile.NodeSetFiles = localProfile.NodeSetFiles;
                    cloudLibProfile.HasLocalProfile = true;
                }
            }
            foreach (var cloudProfile in result.Where(p => p.ID == null || p.ID == 0))
            {
                // Need better solution to avoid conflict with local IDs
                cloudProfile.ID = (int)long.Parse(cloudProfile.CloudLibraryId);
            }

            var lastCursor = $"{cloudLibCursor},{localCursor}";
            bool hasMoreData = !(
                bFullResultCloud && pendingCloudLibProfiles?.Any() != true
                && bFullResultLocal && pendingLocalProfiles?.Any() != true);
            var dalResult = new DALResult<CloudLibProfileModel>
            {
                Count = totalCount,
                Data = result,
                StartCursor = !model.PageBackwards ? firstCursor : lastCursor,
                EndCursor = !model.PageBackwards ? lastCursor : firstCursor,
                HasNextPage = (model.PageBackwards && model.Cursor != null) || hasMoreData,
                HasPreviousPage = !(model.PageBackwards && model.Cursor != null) || hasMoreData,
            };
            return Ok(dalResult);
        }

        /// <summary>
        /// Search profiles library for profiles matching criteria passed in. This is a simple search field and 
        /// this will check against several profile fields and return results. 
        /// </summary>
        /// <remarks>Items in profiles library will not have an author id</remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("cloudlibrary/import")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> ImportFromCloudLibrary([FromBody] List<IdStringModel> model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|ImportFromCloudLibrary|Invalid model");
                return BadRequest("Profile|CloudLibrary||Import|Invalid model");
            }

            List<ImportOPCModel> importModels = new();
            foreach (var modelId in model.Select(m => m.ID))
            {
                try
                {
                    var nodeSetToImport = await _cloudLibDal.DownloadAsync(modelId);
                    if (nodeSetToImport == null)
                    {
                        _logger.LogWarning($"ProfileController|ImportFromCloudLibrary|Did not find nodeset in Cloud Library: {modelId}.");
                        return Ok(
                            new ResultMessageWithDataModel()
                            {
                                IsSuccess = false,
                                Message = "NodeSet not found in Cloud Library."
                            }
                        );
                    }
                    var importModel = new ImportOPCModel
                    {
                        Data = nodeSetToImport.NodesetXml,
                        FileName = nodeSetToImport.Namespace,
                        CloudLibraryId = nodeSetToImport.CloudLibraryId,
                    };
                    importModels.Add(importModel);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ProfileController|ImportFromCloudLibrary|Failed to download from Cloud Library: {modelId} {ex.Message}.");
                    return Ok(
                        new ResultMessageWithDataModel()
                        {
                            IsSuccess = false,
                            Message = "Error downloading NodeSet from Cloud Library."
                        }
                    );
                }
            }

            return await Import(importModels);
        }

        /// <summary>
        /// Publishes a profile to the Cloud Library 
        /// This endpoint accepts the profile id and will look up the profile in the DB.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("cloudlibrary/publish")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> PublishToCloudLibrary([FromBody] IdIntModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|PublishToCloudLibrary|Invalid model");
                return BadRequest("Profile|CloudLibrary||Publish|Invalid model");
            }

            var profile = _dal.GetById(model.ID, base.DalUserToken);

            if (profile == null)
            {
                _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Failed to publish : {model.ID}. Profile not found.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Profile not found."
                    }
                );
            }

            // Prevent publishing of profiles that have no items.
            int cItems = _dalProfileType.GetCountByProfileIdAsync(model.ID, base.DalUserToken).Result;
            if (cItems == 0)
            {
                _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Cannot publish a profile that contains no type definitions : {model.ID}. No profile type items found.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Cannot publish a profile that contains no type definitions."
                    }
                );
            }

            // Validation complete - publication can proceed.
            return Ok(await PublishToCloudLibrary(profile));
        }

        /// <summary>
        /// Save the current model and then publish to CloudLib
        /// Publishes a profile to the Cloud Library. 
        /// This endpoint accepts the entire profile model.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("cloudlibrary/saveandpublish")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> SaveAndPublishToCloudLibrary([FromBody] ProfileModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|PublishToCloudLibrary|Invalid model");
                return BadRequest("Profile|CloudLibrary||Publish|Invalid model");
            }

            //first do the save and then do the publish. if save fails, return that result and end
            ResultMessageWithDataModel resultSave = await UpdateCommon(model);
            if (!resultSave.IsSuccess) return Ok(resultSave);


            var profile = _dal.GetById((int)model.ID, base.DalUserToken);

            if (profile == null)
            {
                _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Failed to publish : {model.ID}. Profile not found.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Profile not found."
                    }
                );
            }

            // Prevent publishing of profiles that have no items.
            int cItems = _dalProfileType.GetCountByProfileIdAsync((int)model.ID, base.DalUserToken).Result;
            if (cItems == 0)
            {
                _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Cannot publish a profile that contains no type definitions : {model.ID}. No profile type items found.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Cannot publish a profile that contains no type definitions."
                    }
                );
            }

            //now do the publish
            return Ok(await PublishToCloudLibrary(model));
        }

        /// <summary>
        /// Publishes a profile to the Cloud Library. 
        /// This endpoint accepts the entire profile model.
        /// </summary>
        /// <remarks>Save optional depending on caller</remarks>
        /// <param name="model"></param>
        /// <returns></returns>
        private async Task<ResultMessageWithDataModel> PublishToCloudLibrary(ProfileModel model)
        {
            try
            {
                var profile = _dal.GetById(model.ID.Value, base.DalUserToken);
                if (profile == null)
                {
                    _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Failed to publish : {model.ID.Value}. Profile not found.");
                    return new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Profile not found."
                    };
                }

                if (!_permissibleLicenses.Contains(profile.License))
                {
                    return new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = $"License must be {string.Join(" or ", _permissibleLicenses)}."
                    };

                }

                var exportResult = await Export(new ExportRequestModel { ID = model.ID.Value }).ConfigureAwait(false);
                var exportedNodeSet = (exportResult as OkObjectResult)?.Value as ResultMessageExportModel;
                if (exportedNodeSet == null || !exportedNodeSet.IsSuccess)
                {
                    _logger.LogWarning($"ProfileController|PublishToCloudLibrary|Failed to publish - cannot export : {model.ID}.");
                    return new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Failed to export."
                    };
                }
                var cloudLibProfile = CloudLibProfileModel.MapFromProfile(profile);

                // Add profile designer user information to the profile
                //Todo: Just user LocalUser which already has this info
                var user = _dalUser.GetById(base.DalUserToken.UserId, base.DalUserToken);
                if (cloudLibProfile.AdditionalProperties == null)
                {
                    cloudLibProfile.AdditionalProperties = new();
                }
                // TODO: remove email/displayname once we have CloudLib admin UI
                var userInfoProp = new AdditionalProperty
                {
                    Name = ICloudLibDal<CloudLibProfileModel>.strCESMIIUserInfo,
                    Value = $"{user.Email}, {user.DisplayName}, {user.ObjectIdAAD}, PD{base.DalUserToken.UserId}",
                };
                cloudLibProfile.AdditionalProperties.RemoveAll(p => p.Name == userInfoProp.Name);
                cloudLibProfile.AdditionalProperties.Add(userInfoProp);

                try
                {
                    var cloudLibId = await _cloudLibDal.UploadAsync(cloudLibProfile, exportedNodeSet.Data as string);

                    profile.CloudLibraryId = cloudLibId;
                    profile.CloudLibPendingApproval = true;
                    await _dal.UpdateAsync(profile, base.DalUserToken);
                }
                catch (UploadException ex)
                {
                    _logger.LogError($"ProfileController|PublishToCloudLibrary|Failed to publish - cannot upload: {model.ID.Value} {ex.Message}.");
                    return new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = ex.Message,
                    };
                }

                // notify recipient of new profile to review
                _ = _cloudLibUtil.EmailPublishNotification(this,profile, LocalUser ); // Run asynchronously

                return new ResultMessageWithDataModel()
                {
                    IsSuccess = true,
                    Message = "Published to Cloud Library, pending approval.",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"ProfileController|PublishToCloudLibrary|Failed to publish - Unhandled exception: {model.ID} {ex.Message}.");
                return new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = ex.Message
                };
            }
        }


        /// <summary>
        /// Get an all profile count and a count of my profiles. 
        /// </summary>
        /// <returns></returns>
        [HttpGet, Route("Count")]
        [ProducesResponseType(200, Type = typeof(ProfileCountModel))]
        [ProducesResponseType(400)]
        public IActionResult GetCounts()
        {
            var all = _dal.Count(s => !string.IsNullOrEmpty(s.CloudLibraryId), base.DalUserToken);
            var mine = _dal.Count(s => string.IsNullOrEmpty(s.CloudLibraryId) && s.AuthorId.HasValue && s.AuthorId.Value.Equals(DalUserToken.UserId), base.DalUserToken);
            return Ok(new ProfileCountModel() { All = all, Mine = mine });
        }

        /// <summary>
        /// Add a profile from the front end. This is different than importing a nodeset XML file. 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("Add")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> Add([FromBody] ProfileModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|Add|Invalid model (null)");
                return BadRequest($"Invalid model (null). Check Publish Date formatting.");
            }

            // Keep PostgreSQL happy - Make sure UTC date gets sent to "date with timezone" timestamp field.
            ConvertPublishDateToUTC(model);

            //test for unique namespace/owner id/publish date combo
            if (!IsValidModel(model))
            {
                return Ok(new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "There is already a profile with this namespace and publish date combination. Enter a different namespace or publish date.",
                    Data = null
                });
            }

            //set some values server side 
            model.AuthorId = base.DalUserToken.UserId;
            model.NodeSetFiles = null;
            model.CloudLibraryId = null;

            //re-validate
            ModelState.Clear();
            TryValidateModel(model);

            if (!ModelState.IsValid)
            {
                var errors = ExtractModelStateErrors();
                return BadRequest("The profile record is invalid. Please correct the following: " + errors.ToString());
            }

            var id = await _dal.AddAsync(model, base.DalUserToken);
            if (id < 0)
            {
                _logger.LogWarning($"ProfileController|Add|Could not add profile item.");
                return BadRequest("Could not add profile item.");
            }
            _logger.LogInformation($"ProfileController|Add|Added profile item. Id:{id}.");

            return Ok(new ResultMessageWithDataModel()
            {
                IsSuccess = true,
                Message = "Item was added.",
                Data = id
            });
        }


        /// <summary>
        /// Validate the profile model has unique name and publish date combination
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("validate")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public IActionResult ValidateModel([FromBody] ProfileModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|ValidateModel|Invalid model (null)");
                return BadRequest($"Invalid model (null). Check Publish Date formatting.");
            }

            //test for unique namespace/owner id/publish date combo
            if (!IsValidModel(model))
            {
                return Ok(new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "There is already a profile with this namespace and publish date combination. Enter a different namespace or publish date.",
                    Data = null
                });
            }

            //if we get here, passed
            return Ok(new ResultMessageWithDataModel()
            {
                IsSuccess = true,
                Message = "Valid",
                Data = null
            });
        }

        private bool IsValidModel(ProfileModel model)
        {
            //test for unique namespace/owner id/publish date combo, 0 count means all good
            return _dal.Count(x => x.Namespace.ToLower().Equals(model.Namespace.ToLower()) &&
                             x.OwnerId.HasValue && x.OwnerId.Value.Equals(LocalUser.ID) &&
                             (x.PublishDate == model.PublishDate)
                            , base.DalUserToken) == 0;
        }

        /// <summary>
        /// Update an existing nodeset that is maintained within this system.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("Update")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> Update([FromBody] ProfileModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|Add|Invalid model (null)");
                return BadRequest($"Invalid model (null). Check Publish Date formatting.");
            }

            return Ok(await UpdateCommon(model));
        }

        /// <summary>
        /// Update an existing nodeset that is maintained within this system.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        private async Task<ResultMessageWithDataModel> UpdateCommon(ProfileModel model)
        {
            // Keep PostgreSQL happy - Make sure UTC date gets sent to "date with timezone" timestamp field.
            ConvertPublishDateToUTC(model);

            //test for unique namespace/owner id/publish date combo
            if (_dal.Count(x => !x.ID.Equals(model.ID) && x.Namespace.ToLower().Equals(model.Namespace.ToLower()) &&
                             x.OwnerId.HasValue && x.OwnerId.Value.Equals(LocalUser.ID)
                             && ((!model.PublishDate.HasValue && !x.PublishDate.HasValue)
                                    || (model.PublishDate.HasValue && x.PublishDate.HasValue && model.PublishDate.Value.Equals(x.PublishDate.Value)))
                            //&& (!x.PublishDate.HasValue ? new DateTime(0) : x.PublishDate.Value.Date).Equals(!model.PublishDate.HasValue ? new DateTime(0) : model.PublishDate.Value.Date)
                            , base.DalUserToken) > 0)
            {
                return new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "There is already a profile with this namespace and publish date combination. Enter a different namespace or publish date.",
                    Data = null
                };
            }

            var item = _dal.GetById(model.ID.Value, base.DalUserToken);
            //can't update an item that is not yours
            if (!item.AuthorId.Equals(LocalUser.ID))
            {
                _logger.LogWarning($"ProfileController|Update|AuthorId {model.AuthorId} of item {model.ID} is different than User Id {LocalUser.ID} making update.");

                return new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "Invalid operation. You cannot update a profile that you did not author.",
                    Data = null
                };
            }

            //re-validate
            ModelState.Clear();
            TryValidateModel(model);

            if (!ModelState.IsValid)
            {
                var errors = ExtractModelStateErrors();
                return new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "The profile record is invalid. Please correct the following: " + errors.ToString(),
                    Data = null
                };
            }

            var result = await _dal.UpdateAsync(model, base.DalUserToken);
            if (result < 0)
            {
                _logger.LogWarning($"ProfileController|Update|Could not update profile item. Invalid id:{model.ID}.");
                return new ResultMessageWithDataModel()
                {
                    IsSuccess = false,
                    Message = "Could not update item. Invalid id.",
                    Data = null
                };
            }
            _logger.LogInformation($"ProfileController|Update|Updated item. Id:{model.ID}.");

            //TBD - come back to this. Race condition. timing error - issue with update not completing and then calling get
            //      issue is child item's virtual property is null unless we give it enough time to complete update process. 
            //return result object plus item.
            return new ResultMessageWithDataModel()
            {
                IsSuccess = true,
                Message = "Item was updated.",
                //Data = new JRaw(JsonConvert.SerializeObject(this.GetItem(model.ID))
                //Data = new JRaw(JsonConvert.SerializeObject(new IdIntModel() { ID = model.ID }))  //TBD - returning empty array - why?
                Data = model.ID
            };
        }

        /// <summary>
        /// Delete an existing nodeset. 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("Delete")]
        [ProducesResponseType(200, Type = typeof(ResultMessageModel))]
        public async Task<IActionResult> Delete([FromBody] IdIntModel model)
        {
            //This also deletes all associated type defs, attributes, custom data types, compositions, interfaces 
            //associated with this profile
            try
            {
                var result = await _dal.DeleteAsync(model.ID, base.DalUserToken);
                if (result <= 0)
                {
                    if (User.IsInRole("cesmii.profiledesigner.admin"))
                    {
                        _logger.LogWarning($"ProfileController|Delete|Could not delete item. Invalid id:{model.ID}. Trying again as global user for administrator {base.DalUserToken.UserId}");
                        // try again with the global user token so that admin can delete global nodesets
                        var globalToken = new UserToken { UserId = -1 };
                        result = await _dal.DeleteAsync(model.ID, globalToken);
                        if (result <= 0)
                        {
                            _logger.LogWarning($"ProfileController|Delete|Could not delete item for admin user {base.DalUserToken.UserId}. Invalid id:{model.ID}.");
                            return BadRequest("Could not delete item. Invalid id.");
                        }
                        _logger.LogInformation($"ProfileController|Delete|Deleted item for administrator {base.DalUserToken.UserId}. Id:{model.ID}.");
                    }
                    else
                    {
                        _logger.LogWarning($"ProfileController|Delete|Could not delete item. Invalid id:{model.ID}.");
                        return BadRequest("Could not delete item. Invalid id.");
                    }
                }
                else
                {
                    _logger.LogInformation($"ProfileController|Delete|Deleted item. Id:{model.ID}.");
                }
            }
            catch (Npgsql.PostgresException eDB)
            {
                //trap foreign key error and let user know there is a dependency issue
                _logger.LogCritical($"ProfileController|Delete|Id:{model.ID}.", eDB);
                if (eDB.Message.ToLower().Contains("violates foreign key constraint"))
                {
                    return Ok(new ResultMessageModel()
                    {
                        IsSuccess = false,
                        Message = "This profile cannot be deleted because something else depends on it. Check that no other profiles nor type definitions depend on this profile or this profile's type definitions. "
                    });
                }
                //some other db issue
                return Ok(new ResultMessageModel()
                {
                    IsSuccess = false,
                    Message = "Please contact your system administrator."
                });
            }

            //return success message object
            return Ok(new ResultMessageModel() { IsSuccess = true, Message = "Item was deleted." });
        }

        /// <summary>
        /// Delete one or many nodesets. 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("DeleteMany")]
        [ProducesResponseType(200, Type = typeof(ResultMessageModel))]
        public async Task<IActionResult> DeleteMany([FromBody] List<IdIntModel> model)
        {
            //check that this user is author of all the items they are trying to delete
            var ids = model.Select(x => x.ID).ToList();

            //This also deletes all associated type defs, attributes, custom data types, compositions, interfaces 
            //associated with this nodeset and its profiles
            try
            {
                var result = await _dal.DeleteManyAsync(model.Select(x => x.ID).ToList(), base.DalUserToken);
                if (result < 0)
                {
                    _logger.LogWarning($"ProfileController|DeleteMany|Could not delete item(s). Invalid model:{string.Join(", ", ids)}.");
                    return BadRequest("Could not delete item(s). Invalid id(s).");
                }
                else
                {
                    _logger.LogInformation($"ProfileController|Delete|Deleted nodeset item(s). Id:{string.Join(", ", model)}.");
                    //return success message object
                    return Ok(new ResultMessageModel() { IsSuccess = true, Message = model.Count == 1 ? "Item was deleted." : $"{model.Count} Items were deleted." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
                return BadRequest("Could not delete item(s). Invalid id(s).");
            }
        }

        /*
        /// <summary>
        /// Flush the UA Cache 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [Obsolete("Is this needed anymore?")]
        [HttpPost, Route("UAFlushCache")]
        [ProducesResponseType(200, Type = typeof(List<ProfileTypeDefinitionModel>))]
        public Task<IActionResult> UAFlushCache()
        {
            _logger.LogInformation($"ProfileController|Flush NodeSets Importer Cache. .");
            var myNodeSetCache = new UANodeSetFileCache();
            myNodeSetCache.FlushCache();
            //return success message object
            return Task.FromResult<IActionResult>(Ok(new ResultMessageModel() { IsSuccess = true, Message = "Item was deleted." }));
        }
        */

        /// <summary>
        /// Re-purposed import items downloaded from Cloud Library
        /// </summary>
        /// <remarks>Non-standard nodesets are associated with the user doing the uploading. 
        /// Standard OPC UA nodesets will go into the library of nodesets visible to all.
        /// This method formerly named ImportMyOpcUaNodeSet.
        /// </remarks>
        /// <param name="nodeSetXmlList"></param>
        /// <returns>Return result model with an isSuccess indicator.</returns>
        //[HttpPost, Route("Import")]
        //[ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        private async Task<IActionResult> Import([FromBody] List<ImportOPCModel> model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ExtractModelStateErrors();
                _logger.LogCritical($"ProfileController|Import|User Id:{LocalUser.ID}, Errors: {errors}");
                
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "The nodeset data is invalid."
                    });
            }

            if (model == null || model.Count == 0)
            {
                _logger.LogWarning($"ProfileController|Import|No nodeset files to import. User Id:{LocalUser.ID}.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "No nodesets to import."
                    }
                );
            }

            _logger.LogInformation($"ProfileController|ImportMyOpcUaProfile|Importing {model.Count} nodeset files. User Id:{LocalUser.ID}.");

            //pass in the author id as current user
            //kick off background process, logid is returned immediately so front end can track progress...
            var userInfo = new ImportUserModel() { User = LocalUser, UserToken = base.DalUserToken };
            var logId = await _svcImport.ImportOpcUaNodeSetAsync(model, userInfo, allowMultiVersion: false, upgradePreviousVersions: false);

            return Ok(
                new ResultMessageWithDataModel()
                {
                    IsSuccess = true,
                    Message = "Import is processing...",
                    Data = logId
                }
            );
        }

        /*MOVED TO ImportLogController
        /// <summary>
        /// Import OPC UA nodeset uploaded by front end and upgrade any prior versions to this version. There may be multiple files being uploaded. 
        /// </summary>
        /// <remarks>Non-standard nodesets are associated with the user doing the uploading. 
        /// Standard OPC UA nodesets will go into the library of nodesets visible to all.
        /// </remarks>
        /// <param name="nodeSetXmlList"></param>
        /// <returns>Return result model with an isSuccess indicator.</returns>
        [HttpPost, Route("ImportUpgrade")]
        [Authorize(Roles = "cesmii.profiledesigner.admin")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> ImportWithUpgrade([FromBody] List<ImportOPCModel> model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ExtractModelStateErrors();
                _logger.LogCritical($"ProfileController|Import|User Id:{LocalUser.ID}, Errors: {errors}");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "The nodeset data is invalid."
                    }
                );
            }

            if (model == null || model.Count == 0)
            {
                _logger.LogWarning($"ProfileController|Import|No nodeset files to import. User Id:{LocalUser.ID}.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "No nodesets to import."
                    }
                );
            }

            _logger.LogInformation($"ProfileController|ImportMyOpcUaProfile|Importing {model.Count} nodeset files. User Id:{LocalUser.ID}.");

            //pass in the author id as current user
            //kick off background process, logid is returned immediately so front end can track progress...
            var userInfo = new ImportUserModel() { User = LocalUser, UserToken = base.DalUserToken };
            var logId = await _svcImport.ImportOpcUaNodeSet(model, userInfo, true, true);

            return Ok(
                new ResultMessageWithDataModel()
                {
                    IsSuccess = true,
                    Message = "Import is processing...",
                    Data = logId
                }
            );
        }
        */

        /// <summary>
        /// Exports all type definitions in a profile 
        /// </summary>
        /// <param name="model"></param>
        /// <returns>Returns the OPC UA models in XML format</returns>
        [HttpPost, Route("Export")]
        [ProducesResponseType(200, Type = typeof(ResultMessageExportModel))]
        public Task<IActionResult> Export([FromBody] ExportRequestModel model)
        {
            //get profile to export
            var sw = Stopwatch.StartNew();
            _logger.LogTrace("Starting export");
            var item = _dal.GetById(model.ID, base.DalUserToken);
            if (item == null)
            {
                //return Task.FromResult(new ResultMessageWithDataModel()
                return Task.FromResult<IActionResult>(Ok(new ResultMessageExportModel()
                {
                    IsSuccess = false,
                    Message = "Profile not found."
                }
                ));
            }

            // Populate the OPC model into a new importer instance
            try
            {
                string result = null;

                _logger.LogTrace($"Timestamp||Export||Starting: {sw.Elapsed}");
                bool bIncludeRequiredModels = model.Format?.ToUpper() == "AASX";

                bool forceReexport = model.ForceReexport || model.Format?.ToUpper() == "SMIPJSON";

                var exportedNodeSets = _exporter.ExportNodeSet(item, base.DalUserToken, null, bIncludeRequiredModels, forceReexport);
                if (exportedNodeSets != null)
                {
                    if (model.Format?.ToUpper() == "AASX")
                    {
                        var aasxPackage = AASXGenerator.GenerateAAS(exportedNodeSets.Select(n => (n.nodeSet, n.xml)).ToList());
                        if (aasxPackage != null)
                        {
                            return Task.FromResult<IActionResult>(Ok(new ResultMessageExportModel()
                            {
                                IsSuccess = true,
                                Message = "",
                                Data = aasxPackage,
                                Warnings = item.ImportWarnings.Select(x => x.Message).ToList()
                            }));
                        }
                    }
                    else if (model.Format?.ToUpper() == "SMIPJSON")
                    {
                        var modelToExport = exportedNodeSets.FirstOrDefault().model;
                        var smipJson = NodeModelExportToSmip.ExportToSmip(modelToExport);
                        result = JsonConvert.SerializeObject(smipJson, Formatting.Indented);
                    }
                    else
                    {
                        _logger.LogTrace($"Timestamp||Export||Nodeset Stream generated: {sw.Elapsed}");
                        result = exportedNodeSets.FirstOrDefault().xml;
                        _logger.LogTrace($"Timestamp||Export||Data Converted to Response: {sw.Elapsed}");
                        //TBD - read and include the required models in a ZIP file, optionally?
                        //TBD - get the warnings that were logged on import and publish them here. 
                    }
                }
                else
                {
                    return Task.FromResult<IActionResult>(Ok(new ResultMessageExportModel()
                    {
                        IsSuccess = false,
                        Message = "An error occurred downloading the profile."
                    }));
                }

                return Task.FromResult<IActionResult>(Ok(new ResultMessageExportModel()
                {
                    IsSuccess = true,
                    Message = "",
                    Data = result,
                    Warnings = item.ImportWarnings.Select(x => x.Message).ToList()
                }));
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, $"ProfileController|Export|Failed:{ex.Message}");
                _logger.LogTrace($"Timestamp||Export||Error: {sw.Elapsed}|| {ex.Message}");
                return Task.FromResult<IActionResult>(Ok(new ResultMessageExportModel()
                {
                    IsSuccess = false,
                    Message = $"Technical details: {ex.Message}"
                }));
            }
        }

        #region Helper methods
        internal int? DeleteInternalTestHook(IdIntModel model)
        {
            return _dal.DeleteAsync(model.ID, new UserToken { UserId = -1 }).Result;
        }

        private static void ConvertPublishDateToUTC(ProfileModel model)
        {
            if (model.PublishDate != null && model.PublishDate?.Kind != DateTimeKind.Utc)
            {
                //// Set time to noon to avoid loss of a day when converting to UTC
                //if (model.PublishDate.Value.Hour < 12)
                //{
                //    int h = model.PublishDate.Value.Hour;
                //    DateTime dt = new DateTime(model.PublishDate.Value.Year, model.PublishDate.Value.Month, model.PublishDate.Value.Day, 12, 0, 0);
                //    model.PublishDate = dt;
                //}
                if (model.PublishDate?.Kind == DateTimeKind.Unspecified)
                {
                    model.PublishDate = DateTime.SpecifyKind(model.PublishDate.Value, DateTimeKind.Utc);
                }
                else
                {
                    model.PublishDate = model.PublishDate?.ToUniversalTime();
                }
            }
        }

        #endregion

    }
}
