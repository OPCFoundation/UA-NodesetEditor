using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

using CESMII.ProfileDesigner.Common;
using CESMII.ProfileDesigner.DAL.Models;
using CESMII.ProfileDesigner.DAL;
using CESMII.ProfileDesigner.Api.Shared.Models;
using CESMII.ProfileDesigner.Api.Shared.Controllers;
using CESMII.ProfileDesigner.Data.Entities;
using CESMII.ProfileDesigner.Common.Enums;
using CESMII.ProfileDesigner.Api.Utils;
using CESMII.Common.CloudLibClient;

namespace CESMII.ProfileDesigner.Api.Controllers
{

    [Route("api/[controller]")]
    [Authorize]
    public class CloudLibraryController : BaseController<UserController>
    {
        private readonly ICloudLibDal<CloudLibProfileModel> _dalCloudLib;
        private readonly IDal<Profile, ProfileModel> _dalProfile;
        private readonly CloudLibraryUtil _cloudLibUtil;

        public CloudLibraryController(
            ICloudLibDal<CloudLibProfileModel> cloudLibDal,
            IDal<Profile, ProfileModel> profileDal,
            UserDAL userDal,
            ConfigUtil config, 
            CloudLibraryUtil cloudLibUtil,
            ILogger<UserController> logger)
            : base(config, logger, userDal)
        {
            _dalCloudLib = cloudLibDal;
            _dalProfile = profileDal;
            _cloudLibUtil = cloudLibUtil;
        }

        // New model for pagination parameters
        public class PendingApprovalsFilterModel
        {
            public string Query { get; set; }
            public int Skip { get; set; } = 0;
            public int Take { get; set; } = 25;
            public string Cursor { get; set; }
            public bool PageBackwards { get; set; } = false;
        }

        [HttpPost, Route("pendingapprovals")]
        [Authorize(Roles = "cesmii.profiledesigner.admin")]
        [ProducesResponseType(200, Type = typeof(DALResult<CloudLibProfileModel>))]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetPendingApprovalAsync([FromBody] PendingApprovalsFilterModel model)
        {
            // Set defaults if model is null
            if (model == null)
            {
                model = new PendingApprovalsFilterModel();
            }

            // Validate Take parameter to prevent excessive requests
            if (model.Take > 100)
            {
                model.Take = 100;
            }
            if (model.Take <= 0)
            {
                model.Take = 25;
            }

            try
            {
                var pendingNodeSetsResult = await _dalCloudLib.GetNodeSetsPendingApprovalAsync(
                    model.Take, 
                    model.Cursor, 
                    model.PageBackwards, 
                    additionalProperty: new AdditionalProperty 
                    { 
                        Name = ICloudLibDal<CloudLibProfileModel>.strCESMIIUserInfo, 
                        Value = $"PD{base.DalUserToken.UserId}" 
                    });

                if (pendingNodeSetsResult == null)
                {
                    return BadRequest($"No records found.");
                }

                // Apply search filter if provided
                var filteredNodes = pendingNodeSetsResult.Nodes.AsEnumerable();
                if (!string.IsNullOrEmpty(model.Query))
                {
                    var query = model.Query.ToLower();
                    filteredNodes = filteredNodes.Where(x => 
                        (x.Title != null && x.Title.ToLower().Contains(query)) ||
                        (x.Namespace != null && x.Namespace.ToLower().Contains(query)) ||
                        (x.Description != null && x.Description.ToLower().Contains(query)) ||
                        (x.ContributorName != null && x.ContributorName.ToLower().Contains(query)) ||
                        (x.License != null && x.License.ToLower().Contains(query)) ||
                        (x.Author != null && x.Author.DisplayName != null && x.Author.DisplayName.ToLower().Contains(query))
                    );
                }

                // Apply skip/take for client-side pagination if search is applied
                if (!string.IsNullOrEmpty(model.Query) && model.Skip > 0)
                {
                    filteredNodes = filteredNodes.Skip(model.Skip);
                }

                var resultList = filteredNodes
                    .OrderBy(x => x.ProfileState)
                    .ThenBy(x => (string.IsNullOrEmpty(x.Title) 
                            ? x.Namespace.Replace("https://","").Replace("http://", "") : x.Title).Trim())
                    .ThenBy(x => x.Namespace.Replace("https://", "").Replace("http://", "").Trim())
                    .ThenBy(x => x.PublishDate)
                    .ToList();

                DALResult<CloudLibProfileModel> result = new DALResult<CloudLibProfileModel>
                {
                    Count = string.IsNullOrEmpty(model.Query) ? pendingNodeSetsResult.TotalCount : resultList.Count,
                    Data = resultList,
                    EndCursor = pendingNodeSetsResult.PageInfo.EndCursor,
                    StartCursor = pendingNodeSetsResult.PageInfo.StartCursor,
                    HasNextPage = pendingNodeSetsResult.PageInfo.HasNextPage,
                    HasPreviousPage = pendingNodeSetsResult.PageInfo.HasPreviousPage,
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending approvals");
                return StatusCode(500, "Error retrieving pending approvals");
            }
        }

        /// <summary>
        /// Changes publish status of a profile in the Cloud Library. 
        /// Could be approved, rejected, cancelled.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("approve")]
        [Authorize(Roles = "cesmii.profiledesigner.admin")]
        [ProducesResponseType(200, Type = typeof(CloudLibProfileModel))]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ApproveProfileAsync([FromBody] ApprovalModel model)
        {
            if (model == null)
            {
                _logger.LogWarning($"ProfileController|ApproveProfile|Invalid model (null)");
                return BadRequest($"Invalid model (null)");
            }

            if (string.IsNullOrEmpty(model.ID))
            {
                _logger.LogWarning($"ProfileController|ApproveProfile|Failed to approve : {model.ID}. Profile has no cloud library id.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Profile not in cloud library."
                    }
                );

            }

            var approvedNodeSet = await _dalCloudLib.UpdateApprovalStatusAsync(model.ID, 
                CloudLibraryUtil.GetApprovalStatusString(model.ApproveState), model.ApprovalDescription);
            if (approvedNodeSet == null)
            {
                return BadRequest($"Approval update failed.");
            }

            ///
            ///  When a profile status changes, send an email to notify the author.
            ///
            
            var profilelist = _dalProfile.Where(x => x.CloudLibraryId.Equals(model.ID), // Get the profile, using the cloud library id.
                    base.DalUserToken, null, null, false, true).Data;

            if (profilelist != null && profilelist.Count > 0)
            {
                var profile = profilelist.FirstOrDefault();
                
                int idUser = (int)profile.AuthorId;  // Get the authoring user
                UserModel umAuthor = _dalUser.GetById(idUser, new UserToken());

                string strNewStatus = model.ApproveState == ProfileStateEnum.CloudLibCancelled ? "Cancel Profile Submission and Remove from Submission Queue" :
                                      model.ApproveState == ProfileStateEnum.CloudLibPending ? "Remain in Submission Queue as Pending Submission" :
                                      model.ApproveState.ToString();
                SubmittedProfileModel SubmittedProfile = new SubmittedProfileModel(profile, model, strNewStatus);

                if (model.ApproveState == ProfileStateEnum.CloudLibApproved)
                {
                    await _cloudLibUtil.EmailApprovedNotification(this, profile, SubmittedProfile, umAuthor);   // APPROVED! -- Notify the user.
                }
                else if (model.ApproveState == ProfileStateEnum.CloudLibRejected)
                {
                    await _cloudLibUtil.EmailRejectedNotification(this, profile, SubmittedProfile, umAuthor);   // REJECTED! -- Notify the user.
                }
                else
                {
                    await _cloudLibUtil.EmailStatusChanged(this, profile, SubmittedProfile, umAuthor);          // Something else -- Notify the user.
                }
            }

            return Ok(approvedNodeSet);
        }

        /// <summary>
        /// Cancels a publish profile to the Cloud Library. 
        /// This is called by the authoring user. 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, Route("publishcancel")]
        [ProducesResponseType(200, Type = typeof(ResultMessageWithDataModel))]
        public async Task<IActionResult> CancelPublishToCloudLibrary([FromBody] IdIntModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("ProfileController|CancelPublishToCloudLibrary|Invalid model");
                return BadRequest("Profile|CloudLibrary||PublishCancel|Invalid model");
            }

            try
            {
                var profile = _dalProfile.GetById(model.ID, base.DalUserToken);
                if (profile == null)
                {
                    _logger.LogWarning($"ProfileController|CancelPublishToCloudLibrary|Failed to cancel : {model.ID}. Profile not found.");
                    return Ok(
                        new ResultMessageWithDataModel()
                        {
                            IsSuccess = false,
                            Message = "Profile not found."
                        }
                    );
                }

                try
                {
                    var updatedProfile = await _dalCloudLib.UpdateApprovalStatusAsync(profile.CloudLibraryId,
                        CloudLibraryUtil.GetApprovalStatusString(ProfileStateEnum.CloudLibCancelled), $"Canceled by user {DalUserToken.UserId}");
                    if (updatedProfile == null || updatedProfile.CloudLibApprovalStatus == null ||
                        updatedProfile.CloudLibApprovalStatus == CloudLibraryUtil.GetApprovalStatusString(ProfileStateEnum.CloudLibCancelled))
                    {
                        profile.CloudLibraryId = null;
                        profile.CloudLibPendingApproval = null;
                        await _dalProfile.UpdateAsync(profile, base.DalUserToken);
                    }
                    else
                    {
                        _logger.LogWarning($"ProfileController|CancelPublishToCloudLibrary|Failed to cancel : {model.ID}. Status Update failed.");
                        return Ok(
                            new ResultMessageWithDataModel()
                            {
                                IsSuccess = false,
                                Message = "Status update failed."
                            }
                        );

                    }
                }
                catch (UploadException ex)
                {
                    _logger.LogError($"ProfileController|CancelPublishToCloudLibrary|Failed to cancel publish request to Cloud Library: {model.ID} {ex.Message}.");
                    return Ok(
                        new ResultMessageWithDataModel()
                        {
                            IsSuccess = false,
                            Message = ex.Message,
                        }
                    );
                }

                //notify
                await _cloudLibUtil.EmailCancelNotification(this,profile, LocalUser);

                //return all good
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = true,
                        Message = "Cancelled publish request.",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"ProfileController|CancelPublishToCloudLibrary|Failed to cancel publish request to Cloud Library: {model.ID} {ex.Message}.");
                return Ok(
                    new ResultMessageWithDataModel()
                    {
                        IsSuccess = false,
                        Message = "Error cancelling publish request."
                    }
                );
            }
        }
    }
}