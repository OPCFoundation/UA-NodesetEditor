import React, { useState, useEffect, useRef } from 'react'
import { Helmet } from "react-helmet"
import { axiosInstance } from "../../services/AxiosService";
import { useMsal } from "@azure/msal-react";

import ConfirmationModal from "../../components/ConfirmationModal";

import { AppSettings } from '../../utils/appsettings'
import { generateLogMessageString } from '../../utils/UtilityService'
import GridPager from '../../components/GridPager'
import { useLoadingContext } from "../../components/contexts/LoadingContext";

import HeaderSearch from '../../components/HeaderSearch';
import AdminCloudLibApprovalRow from './AdminCloudLibApprovalRow';
import AdminCloudLibApprovalModal from './AdminCloudLibApprovalModal';
import color from '../../components/Constants';

const CLASS_NAME = "AdminCloudLibApprovalList";

function AdminCloudLibApprovalList() {

    //-------------------------------------------------------------------
    // Region: Initialization
    //-------------------------------------------------------------------
    const { instance } = useMsal();
    const _activeAccount = instance.getActiveAccount();
    const _scrollToRef = useRef(null);
    const [_dataRows, setDataRows] = useState({
        all: [], 
        itemCount: 0, 
        listView: true,
        hasNextPage: false,
        hasPreviousPage: false,
        cursor: null
    });
    const _userPreferences = { pageSize: 25};
    const [_pager, setPager] = useState({ 
        currentPage: 1, 
        pageSize: _userPreferences.pageSize, 
        searchVal: null,
        cursor: null,
        pageBackwards: false
    });
    const { loadingProps, setLoadingProps } = useLoadingContext();
    const [_refreshData, setRefreshData] = useState(0);
    const [_approvalModal, setApprovalModal] = useState({ show: false, item: null, approveState: AppSettings.ProfileStateEnum.Unknown });
    const [_error, setError] = useState({ show: false, message: null, caption: null });

    const caption = 'Admin';

    //-------------------------------------------------------------------
    // Region: Event Handling of child component events
    //-------------------------------------------------------------------
    const handleOnSearchChange = (val) => {
        //raised from header nav
        //console.log(generateLogMessageString('handleOnSearchChange||Search value: ' + val, CLASS_NAME));

        //this will trigger a fetch from the API to pull the data for the filtered criteria
        setPager({ 
            ..._pager, 
            currentPage: 1, 
            searchVal: val,
            cursor: null,
            pageBackwards: false
        });
    };

    const onChangePage = (currentPage, pageSize) => {
        console.log(generateLogMessageString(`onChangePage||Current Page: ${currentPage}, Page Size: ${pageSize}`, CLASS_NAME));

        // Calculate if we're going forward or backward
        const pageBackwards = currentPage < _pager.currentPage;
        
        // For cursor-based pagination, we need to handle navigation differently
        let newCursor = null;
        if (pageBackwards && _dataRows.hasPreviousPage) {
            // Going backward - we'd need to implement reverse cursor logic
            // For now, reset to beginning if going to page 1
            newCursor = currentPage === 1 ? null : _pager.cursor;
        } else if (!pageBackwards && _dataRows.hasNextPage) {
            // Going forward - use the end cursor
            newCursor = _dataRows.cursor;
        }

        //this will trigger a fetch from the API to pull the data for the filtered criteria
        setPager({ 
            ..._pager, 
            currentPage: currentPage, 
            pageSize: pageSize,
            cursor: newCursor,
            pageBackwards: pageBackwards
        });

        //scroll screen to top of grid on page change
        //scroll a bit higher than the top edge so we get some of the header in the view
        window.scrollTo({ top: (_scrollToRef.current.offsetTop - 120), behavior: 'smooth' });

        //preserve choice in local storage
        //setUserPreferencesPageSize(pageSize);
    };

    //-------------------------------------------------------------------
    // Region: Get data 
    //-------------------------------------------------------------------
    useEffect(() => {
        async function fetchData() {
            //show a spinner
            setLoadingProps({ isLoading: true, message: null });

            const url = `cloudlibrary/pendingapprovals`;
            console.log(generateLogMessageString(`useEffect||fetchData||${url}`, CLASS_NAME));

            const data = { 
                Query: _pager.searchVal, 
                Skip: _pager.searchVal ? (_pager.currentPage - 1) * _pager.pageSize : 0, // Only use Skip for search
                Take: _pager.pageSize,
                Cursor: _pager.cursor,
                PageBackwards: _pager.pageBackwards
            };

            await axiosInstance.post(url, data).then(result => {
                if (result.status === 200) {
                    //set state on fetch of data
                    setDataRows({
                        ..._dataRows,
                        all: result.data.data, 
                        itemCount: result.data.count,
                        hasNextPage: result.data.hasNextPage || false,
                        hasPreviousPage: result.data.hasPreviousPage || false,
                        cursor: result.data.endCursor
                    });

                    //hide a spinner
                    setLoadingProps({ isLoading: false, message: null });
                } else {
                    setLoadingProps({
                        isLoading: false, message: null, inlineMessages: [
                            { id: new Date().getTime(), severity: "danger", body: 'An error occurred retrieving these items.', isTimed: true }]
                    });
                }
                //hide a spinner
                setLoadingProps({ isLoading: false, message: null });

            }).catch(e => {
                if ((e.response && e.response.status === 401) || e.toString().indexOf('Network Error') > -1) {
                    //do nothing, this is handled in routes.js using common interceptor
                    //setAuthTicket(null); //the call of this will clear the current user and the token
                }
                else {
                    setLoadingProps({
                        isLoading: false, message: null, inlineMessages: [
                            { id: new Date().getTime(), severity: "danger", body: 'An error occurred retrieving these items.', isTimed: true }]
                    });
                }
            });
        }

        fetchData();

        //this will execute on unmount
        return () => {
            //console.log(generateLogMessageString('useEffect||Cleanup', CLASS_NAME));
        };
        //type passed so that any change to this triggers useEffect to be called again
        //_setMarketplacePageSizePreferences.pageSize - needs to be passed so that useEffects dependency warning is avoided.
    }, [_pager, _refreshData]);

    //-------------------------------------------------------------------
    // Region: Event Handling - approval actions
    //-------------------------------------------------------------------
    const onApprovalStart = (item) => {
        console.log(generateLogMessageString('onApprovalStart', CLASS_NAME));
        setApprovalModal({ show: true, item: item, approveState: AppSettings.ProfileStateEnum.CloudLibApproved });
    };

    const onRejectStart = (item) => {
        console.log(generateLogMessageString('onRejectStart', CLASS_NAME));
        setApprovalModal({ show: true, item: item, approveState: AppSettings.ProfileStateEnum.CloudLibRejected });
    };

    const onCancelStart = (item) => {
        console.log(generateLogMessageString('onCancelStart', CLASS_NAME));
        setApprovalModal({ show: true, item: item, approveState: AppSettings.ProfileStateEnum.CloudLibCanceled });
    };

    const onPendingStart = (item) => {
        console.log(generateLogMessageString('onPendingStart', CLASS_NAME));
        setApprovalModal({ show: true, item: item, approveState: AppSettings.ProfileStateEnum.CloudLibPending });
    };

    const onApprovalConfirm = (approvalData) => {
        console.log(generateLogMessageString('onApprovalConfirm', CLASS_NAME));

        //show a spinner
        setLoadingProps({ isLoading: true, message: "" });

        //perform approval call
        const data = { id: _approvalModal.item.cloudLibraryId, approveState: _approvalModal.approveState, approvalDescription: approvalData.description };
        const url = `cloudlibrary/approve`;
        axiosInstance.post(url, data)  //api allows one or many
            .then(result => {

                if (result.data != null && result.data.cloudLibraryId == _approvalModal.item.cloudLibraryId) {
                    //hide a spinner, show a message
                    setLoadingProps({
                        isLoading: false, message: null, inlineMessages: [
                            {
                                id: new Date().getTime(), severity: "success", body: `Approval status was updated`, isTimed: true
                            }
                        ],
                    });

                    setApprovalModal({ show: false, item: null });
                }
                else {
                    //update spinner, messages
                    setError({ show: true, caption: 'Cloud Library Approve / Reject', message: `An error occurred updating approval status. ${result.data.message}` });
                    setLoadingProps({ isLoading: false, message: null });
                    setApprovalModal({ show: false, item: null });
                }
                setRefreshData(_refreshData + 1);
            })
            .catch(error => {
                //hide a spinner, show a message
                setError({ show: true, caption: 'Cloud Library Approve / Reject', message: `An error occurred updating the approval status.` });
                setLoadingProps({ isLoading: false, message: null });

                console.log(generateLogMessageString('changeApprovalStatus||error||' + JSON.stringify(error), CLASS_NAME, 'error'));
                console.log(error);
                //scroll back to top
                window.scroll({
                    top: 0,
                    left: 0,
                    behavior: 'smooth',
                });
                setApprovalModal({ show: false, item: null });
            });
    };

    //-------------------------------------------------------------------
    // Region: Render helpers
    //-------------------------------------------------------------------
    const renderNoDataRow = () => {
        return (
            <div className="alert alert-info-custom mt-2 mb-2">
                <div className="text-center" >There are no matching items.</div>
            </div>
        );
    }

    //render pagination ui - updated to handle cursor-based pagination
    const renderPagination = () => {
        if (_dataRows == null || _dataRows.all.length === 0) return;
        
        // For search results, use traditional skip/take pagination
        // For non-search, use cursor-based pagination
        const showPagination = _pager.searchVal ? 
            _dataRows.itemCount > _pager.pageSize :
            (_dataRows.hasNextPage || _dataRows.hasPreviousPage || _pager.currentPage > 1);
            
        if (!showPagination) return null;
        
        return <GridPager 
            currentPage={_pager.currentPage} 
            pageSize={_pager.pageSize} 
            itemCount={_dataRows.itemCount} 
            onChangePage={onChangePage}
            hasNextPage={_dataRows.hasNextPage}
            hasPreviousPage={_dataRows.hasPreviousPage}
        />
    }

    //render the main grid
    const renderItemsGrid = () => {
        if (!loadingProps.isLoading && (_dataRows.all == null || _dataRows.all.length === 0)) {
            return (
                renderNoDataRow()
            )
        }
        if ((_dataRows.all == null || _dataRows.all.length === 0)) return;

        return _dataRows.all.map((item) => {
            return (
                <AdminCloudLibApprovalRow key={item.cloudLibraryId} item={item} className={`admin-item-row`}
                    onApprove={onApprovalStart} onReject={onRejectStart} onCancel={onCancelStart} onSetPending={onPendingStart} />
            );
        });
    }

    //render error message as a modal to force user to say ok.
    const renderErrorMessage = () => {

        if (!_error.show) return;

        return (
            <>
                <ConfirmationModal showModal={_error.show} caption={_error.caption} message={_error.message}
                    icon={{ name: "warning", color: color.trinidad }}
                    confirm={null}
                    cancel={{
                        caption: "OK",
                        callback: () => {
                            //console.log(generateLogMessageString(`onErrorMessageOK`, CLASS_NAME));
                            setError({ show: false, caption: null, message: null });
                        },
                        buttonVariant: 'danger'
                    }} />
            </>
        );
    };

    //render the approval modal when show flag is set to true
    //callbacks are tied to each button click to proceed or cancel
    const renderChangeApprovalStatusConfirmation = () => {

        if (!_approvalModal.show) return;

        const caption = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibApproved ? 'Approve Profile' :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibRejected ? 'Reject Profile' :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? 'Cancel Publish Request' : 
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibPending ? 'Return to Pending Queue' : 'Change Approval Status'}`;
        const btnOkCaption = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibApproved ? 'Approve' :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibRejected ? 'Reject' : 
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? 'Cancel Publish Request' : 
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibPending ? 'Requeue' : 'Yes'}`;

        const strCanUndo = `You are changing the status for '${_approvalModal.item.title}'. The author is notified of this change and your reviewer comments (below) will be included with the notification message. This action CAN be undone. Are you sure?`;
        const strCannotUndo = `You are changing status for '${_approvalModal.item.title}'. The author is notified of this change and your reviewer comments (below) will be included with the notification message. This action CANNOT be undone. Are you sure?`;
        const message = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibApproved ? strCannotUndo :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibRejected ? strCanUndo :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? strCannotUndo :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibPending ? strCanUndo : strCannotUndo}`;

        // const strIcon = "warning";
        const strIcon = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibApproved ? "check" :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibRejected ? "close" :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? "undo" :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibPending ? "cloud-upload" : "warning"}`;

        // const colorIcon = color.apple;
        const colorIcon = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibApproved ? color.apple :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibRejected ? color.cardinal :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? color.cardinal :
            _approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibPending ? color.amber : color.amber}`;

        const btnCancelCaption = `${_approvalModal.approveState === AppSettings.ProfileStateEnum.CloudLibCanceled ? "Close" : "Cancel" }`;

        return (
            <>
                <AdminCloudLibApprovalModal item={_approvalModal.item} showModal={_approvalModal.show} caption={caption} message={message}
                    icon={{ name: strIcon, color: colorIcon }}
                    confirm={{ caption: btnOkCaption, callback: onApprovalConfirm, buttonVariant: "primary" }}
                    cancel={{
                        caption: btnCancelCaption,
                        callback: () => {
                            console.log(generateLogMessageString(`onChangeApprovalCancel`, CLASS_NAME));
                            setApprovalModal({ show: false, item: null, approveState: AppSettings.ProfileStateEnum.Unknown });
                        },
                        buttonVariant: null
                    }} />
            </>
        );
    };

    //-------------------------------------------------------------------
    // Region: Render final output
    //-------------------------------------------------------------------
    return (
        <>
            <Helmet>
                <title>{AppSettings.Titles.Main + " Admin | " + caption}</title>
            </Helmet>
            <div className="row py-2 pb-4">
                <div className="col-sm-8">
                    <h1>Admin | Cloud Lib Approval Queue</h1>
                </div>
                <div className="col-sm-4 d-flex align-items-center justify-content-end" >
                    <HeaderSearch showAdvancedSearch={false} filterVal={_pager.searchVal == null ? null : _pager.searchVal} onSearch={handleOnSearchChange} searchMode="standard" activeAccount={_activeAccount} />
                </div>
            </div>

            <div className="row pb-2" >
                <div className="col-sm-12 d-flex align-items-center" >
                    {(_dataRows.itemCount != null && _dataRows.itemCount > 0) && 
                        <>
                            <span className="px-2 ml-auto font-weight-bold">{_dataRows.itemCount}{_dataRows.itemCount === 1 ? ' item' : ' items'}</span>
                            {_dataRows.hasNextPage && <span className="px-2 small text-muted">(more available)</span>}
                        </>
                    }
                </div>
            </div>

            <div className="row" >
                <div ref={_scrollToRef} className="col-sm-12 mb-4" >
                    {renderItemsGrid()}
                    {renderPagination()}
                </div>
            </div>
            {renderChangeApprovalStatusConfirmation()}
            {renderErrorMessage()}
        </>
    )
}

export default AdminCloudLibApprovalList;