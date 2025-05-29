import React from 'react';
import { Pagination, Dropdown } from 'react-bootstrap'
import { AppSettings } from '../utils/appsettings';
import { generateLogMessageString } from '../utils/UtilityService'
import './styles/GridPager.scss';

const CLASS_NAME = "GridPager";

function GridPager(props) { //(currentPage, pageSize, itemCount, onChangePage, hasNextPage, hasPreviousPage)
    //-------------------------------------------------------------------
    // Region: Initialization
    //-------------------------------------------------------------------
    const firstCaption = "|<";
    const lastCaption = ">|";
    const previousCaption = "<";
    const nextCaption = ">";
    const minimuPageSize = 10;
    const maxPageDisplay = 8;

    const raiseOnChangePage = props.onChangePage;

    // Support for cursor-based pagination
    const { hasNextPage, hasPreviousPage } = props;

    //-------------------------------------------------------------------
    // Region: Event Handling of child component events
    //-------------------------------------------------------------------
    //change page event handler
    const onPageClick = (pageIndex) => {
        console.log(generateLogMessageString(`onPageClick||Val: ${pageIndex}`, CLASS_NAME));
        //setCurrentPage(e.target.value);
        // call change page function in parent component
        raiseOnChangePage(pageIndex, props.pageSize);
    }

    //on page size change which will trigger a change page event 
    const onPageSizeSelect = (pageSize) => {
        console.log(generateLogMessageString(`onPageSizeSelect||Val: ${pageSize}`, CLASS_NAME));
        //setPager({ currentPage: _pager.currentPage, pageSize: pageSize, itemCount: _pager.itemCount });
        // call change page function in parent component - back to first page with increased page size
        raiseOnChangePage(1, pageSize);
    }

    const getPagerData = (currentPage, pageSize, itemCount) => {

        // default to first page if null
        currentPage = currentPage || 1;
        // default page size if null
        pageSize = pageSize || AppSettings.PageSize;
        // calculate total pages
        var totalPages = Math.ceil(itemCount / pageSize);
        //TBD - do we need this?
        if (currentPage < 1 || currentPage > totalPages) {
            return;
        }

        var startPage, endPage;
        if (totalPages <= 10) {
            // less than 10 total pages so show all
            startPage = 1;
            endPage = totalPages;
        } else {
            // more than 10 total pages so calculate start and end pages
            if (currentPage <= 6) {
                startPage = 1;
                endPage = 10;
            } else if (currentPage + 4 >= totalPages) {
                startPage = totalPages - 9;
                endPage = totalPages;
            } else {
                startPage = currentPage - 5;
                endPage = currentPage + 4;
            }
        }

        // calculate start and end item indexes
        var startIndex = (currentPage - 1) * pageSize;
        var endIndex = Math.min(startIndex + pageSize - 1, itemCount - 1);

        // create an array of pages to ng-repeat in the pager control
        var pages = [...Array((endPage + 1) - startPage).keys()].map(i => startPage + i);

        // return object with all pager properties required by the view
        return {
            itemCount: itemCount,
            currentPage: currentPage,
            pageSize: pageSize,
            totalPages: totalPages,
            startPage: startPage,
            endPage: endPage,
            startIndex: startIndex,
            endIndex: endIndex,
            pages: pages
        };

    }

    const renderPageItems = () => {

        if (props.itemCount <= props.pageSize) {
            // don't display pager if there is only 1 page
            return null;
        }

        //var pager = setPage(_currentPage, _pageSize, _itemCount);
        var pager = getPagerData(props.currentPage, props.pageSize, props.itemCount);
        //it will be null while data is being fetched
        if (pager == null) return;

        var result = [];
        
        // For cursor-based pagination, modify the navigation logic
        const canGoPrevious = hasPreviousPage !== undefined ? hasPreviousPage : props.currentPage > 1;
        const canGoNext = hasNextPage !== undefined ? hasNextPage : props.currentPage < pager.totalPages;
        
        if (pager.totalPages > minimuPageSize) {
            //add first, previous item
            result.push(<Pagination.Item key="first" title="Go to first page" disabled={!canGoPrevious || props.currentPage === 1} onClick={() => onPageClick(1)}>{firstCaption}</Pagination.Item>);
            result.push(<Pagination.Item key="previous" title="Go to previous page" disabled={!canGoPrevious} onClick={() => onPageClick(pager.currentPage - 1)}>{previousCaption}</Pagination.Item>);
        }
        
        //add individual pages 
        var maxHalfway = Math.floor(maxPageDisplay / 2);
        
        // For cursor-based pagination, limit the page range
        const pageLimit = (hasNextPage !== undefined || hasPreviousPage !== undefined) ? 
            Math.min(pager.totalPages, props.currentPage + 2) : pager.totalPages;
            
        for (let pageNum = 1; pageNum <= pageLimit; pageNum++) {
            //if we have more than max pages display...16 pages, we only display 16 around the current page
            if (pager.totalPages <= maxPageDisplay || (pageNum > pager.currentPage - maxHalfway && (pageNum < pager.currentPage + maxHalfway))) {
                result.push(
                    <Pagination.Item key={pageNum} title={`Go to page ${pageNum}`} active={pageNum === pager.currentPage} onClick={() => onPageClick(pageNum)}>
                        {pageNum}
                    </Pagination.Item>
                );
            }
            //show a ...indicator for one item just prior to the group and just after the group
            if (pager.totalPages > maxPageDisplay && (pageNum === pager.currentPage - maxHalfway || (pageNum === pager.currentPage + maxHalfway))) {
                result.push(
                    <Pagination.Item key={pageNum} title="Go to next group of pages" active={pageNum === pager.currentPage} onClick={() => onPageClick(pageNum)}>
                        ...
                    </Pagination.Item>
                );
            }
        }
        //add next, last item
        if (pager.totalPages > minimuPageSize) {
            result.push(<Pagination.Item key="next" title="Go to next page" disabled={!canGoNext} onClick={() => onPageClick(pager.currentPage + 1)}>{nextCaption}</Pagination.Item>);
            // For cursor-based pagination, don't show "last" if we don't know the total
            if (hasNextPage === undefined && hasPreviousPage === undefined) {
                result.push(<Pagination.Item key="last" title="Go to last page" disabled={!canGoNext || props.currentPage === pager.totalPages} onClick={() => onPageClick(pager.totalPages)}>{lastCaption}</Pagination.Item>);
            }
        }
        return (<div className="mr-auto mb-3 mb-lg-0"><Pagination >{result}</Pagination></div>);
    }

    const renderPageSizeOptions = (pageSize) => {

        //make copy of options. If current page size is not represented, add it to options
        var pageSizeOptions = JSON.parse(JSON.stringify(AppSettings.PageSizeOptions));
        if (pageSizeOptions.find(item => { return item === pageSize; }) == null) {
            pageSizeOptions.push(pageSize);
            pageSizeOptions.sort((a, b) => { return parseFloat(a) - parseFloat(b) }); //force numeric sort
        }

        //set current page in drop down toggle
        var toggleHTML = (
            <Dropdown.Toggle key={pageSize} variant="primary" id="dropdown-basic">
                {pageSize} per page
            </Dropdown.Toggle>
        );
        const pageOptionsHTML = pageSizeOptions.map((item) => {
            return (<Dropdown.Item key={item} onSelect={() => onPageSizeSelect(item)} >{item} per page</Dropdown.Item>);
        });

        return (
            <Dropdown className="pagination-dropdown" onClick={(e) => e.stopPropagation()} >
                <span className="label-left">Show</span>
                {toggleHTML}
                <Dropdown.Menu>
                    {pageOptionsHTML}
                </Dropdown.Menu>
            </Dropdown>
        )
    }

    // Helper to show pagination info
    const renderPaginationInfo = () => {
        if (hasNextPage !== undefined || hasPreviousPage !== undefined) {
            // Cursor-based pagination info
            return (
                <div className="pagination-info small text-muted mt-2">
                    Page {props.currentPage}
                    {props.itemCount > 0 && <span> • {props.itemCount} total items</span>}
                    {hasNextPage && <span> • More available</span>}
                </div>
            );
        }
        return null;
    }

    //-------------------------------------------------------------------
    // Region: Render 
    //-------------------------------------------------------------------
    return (
        <div className="pagination-wrapper mt-3 mt-lg-4 d-block d-lg-flex">
            {renderPageItems()}
            {renderPageSizeOptions(props.pageSize)}
            {renderPaginationInfo()}
        </div>
    )
}

export default GridPager;