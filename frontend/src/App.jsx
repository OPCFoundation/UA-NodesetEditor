import * as React from 'react';
import { BrowserRouter } from "react-router-dom"
import { Helmet } from "react-helmet"
import { ErrorBoundary } from 'react-error-boundary'
import axios from "axios"
import axiosInstance from "./services/AxiosService";

import { LoadingContext } from "./components/contexts/LoadingContext";
import Navbar from './components/Navbar'
import { LoadingOverlay } from "./components/LoadingOverlay"
import MainContent from './components/MainContent'
import Footer from './components/Footer'
import { AppSettings } from './utils/appsettings'
import { generateLogMessageString } from './utils/UtilityService'
import ErrorPage from './components/ErrorPage'
import { OnLookupLoad } from './components/OnLookupLoad'
import { useLoginSilent, useRegisterMsalEventCallback } from "./components/OnLoginHandler";

import './App.scss';

const CLASS_NAME = "App";

function App() {
   //console.log('API URL:', import.meta.env.VITE_BASE_API_URL);
   //console.log(generateLogMessageString(`init || ENV || ${import.meta.env.NODE_ENV}`, CLASS_NAME));
   //console.log(generateLogMessageString(`init || API || ${import.meta.env.VITE_BASE_API_URL}`, CLASS_NAME));

   //-------------------------------------------------------------------
   // Region: Initialization
   //-------------------------------------------------------------------
   const { setLoadingProps } = React.useContext(LoadingContext);

   //-------------------------------------------------------------------
   //  TBD - is this the best place for this? 
   //  If a network error occurs (ie API not there), catch it here and handle gracefully.  
   //-------------------------------------------------------------------
   const OnApiResponseError = (err) => {
      //401 - unauthorized - session expired - due to token expiration or unauthorized attempt
      if (err.response && err.response.status === 401) {
         console.log(generateLogMessageString(`axiosInstance.interceptors.response||error||${err.response.status}||${err.config.baseURL}${err.config.url}`, CLASS_NAME));
         setLoadingProps({ isLoading: false, message: null });
      }
      //403 error - user may be allowed to log in but not permitted to perform the API call they are attempting
      else if (err.response && err.response.status === 403) {
         console.log(generateLogMessageString(`axiosInstance.interceptors.response||error||${err.response.status}||${err.config.baseURL}${err.config.url}`, CLASS_NAME));
         setLoadingProps({
            isLoading: false, message: null, inlineMessages: [
               { id: new Date().getTime(), severity: "danger", body: 'You are not permitted to access this area. Please contact your system administrator.', isTimed: true }]
         });
      }
      //no status is our only indicator the API is not up and running
      else if (!err.status) {
         console.log(generateLogMessageString(`axiosInstance.interceptors.response||error||${err.config.baseURL}${err.config.url}||${err}`, CLASS_NAME));
         if (err.message != null && err.message.toLowerCase().indexOf('request aborted') > -1) {
            //do nothing...
         }
         else {
            // API unavailable network error
            setLoadingProps({
               isLoading: false, message: null, isImporting: false, inlineMessages: [
                  { id: new Date().getTime(), severity: "danger", body: 'A system error has occurred. Please contact your system administrator.', isTimed: true }]
            });
         }
      }

   };

   //Catch exceptions in the flow when we use our axiosInstance
   axiosInstance.interceptors.response.use(
      response => {
         return response
      },
      err => {
         OnApiResponseError(err);
         return Promise.reject(err)
      }
   )

   //Catch exceptions in the flow when we use axios not as part of our axiosInstance
   axios.interceptors.response.use(
      response => {
         return response
      },
      err => {
         OnApiResponseError(err);
         return Promise.reject(err)
      }
   )

   //-------------------------------------------------------------------
   // Region: hooks
   // check if user is logged in. If not, attempt silent login
   // if that fails, then user will have to initiate login.
   //-------------------------------------------------------------------
   useLoginSilent();

   //-------------------------------------------------------------------
   // Region: hooks
   // once login completes, determine where to navigate based on outcome
   //-------------------------------------------------------------------
   //useOnLoginComplete();

   //-------------------------------------------------------------------
   // Region: hooks - moved into separate component
   // useEffect - get various lookup data - onlookupLoad component houses the useEffect checks
   //-------------------------------------------------------------------
   OnLookupLoad();

   //-------------------------------------------------------------------
   // Region: add an event callback handler to capture loginredirect message responses
   //              only add the callback once.
   //-------------------------------------------------------------------
   useRegisterMsalEventCallback(setLoadingProps);

   //-------------------------------------------------------------------
   // Region: Render
   //-------------------------------------------------------------------
   return (

      <div>
         <Helmet>
            <title>{AppSettings.Titles.Main}</title>
            <link href="https://fonts.googleapis.com/css2?family=Material+Icons" rel="stylesheet"></link>
         </Helmet>
         <BrowserRouter>
            <Navbar />
            <LoadingOverlay />
            <ErrorBoundary
               FallbackComponent={ErrorPage}
               onReset={() => {
                  // reset the state of your app so the error doesn't happen again
               }}
            >
               <div id="--cesmii-content-wrapper">
                  <MainContent />
               </div>
            </ErrorBoundary>
            <Footer />
         </BrowserRouter>
      </div>

   );
}

export default App;
