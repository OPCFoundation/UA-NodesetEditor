using CESMII.Common;
using CESMII.Common.CloudLibClient;
using CESMII.Common.SelfServiceSignUp;
using CESMII.Common.SelfServiceSignUp.Models;
using CESMII.Common.SelfServiceSignUp.Services;
using CESMII.ProfileDesigner.Api.Shared.Extensions;
using CESMII.ProfileDesigner.Api.Shared.Utils;
using CESMII.ProfileDesigner.DAL;
using CESMII.ProfileDesigner.DAL.Models;
using CESMII.ProfileDesigner.Data.Contexts;
using CESMII.ProfileDesigner.Data.Entities;
using CESMII.ProfileDesigner.Data.Repositories;
using CESMII.ProfileDesigner.OpcUa;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CESMII.OpcUa.NodeSetImporter;

using NLog.Web;
using Opc.Ua.Cloud.Library.Client;
using Microsoft.Identity.Web;
using CESMII.ProfileDesigner.Common.Enums;
using CESMII.ProfileDesigner.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Logging;
using NLog;
using NLog.Web;
using System;

namespace CESMII.ProfileDesigner.Api
{
    public class Program
    {
        private static readonly string _corsPolicyName = "SiteCorsPolicy";

        public static void Main(string[] args)
        {
            var logger = LogManager.Setup()
                .LoadConfigurationFromFile("NLog.config")
                .GetCurrentClassLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);
                
                // 1. Check if Azure injected the Postgres special variable
                var pqsql = Environment.GetEnvironmentVariable("POSTGRESQLCONNSTR_ProfileDesignerDB");

                // 2. If found, map it into the standard ConnectionStrings section
                if (!string.IsNullOrEmpty(pqsql))
                {
                    builder.Configuration["ConnectionStrings:ProfileDesignerDB"] = pqsql;
                }

                var sql = builder.Configuration.GetConnectionString("ProfileDesignerDB");

                #if DEBUG
                builder.Services.AddDbContext<ProfileDesignerPgContext>(options =>
                        options.UseNpgsql(sql)
                        .EnableSensitiveDataLogging());
                #else
                builder.Services.AddDbContext<ProfileDesignerPgContext>(options =>
                        options.UseNpgsql(sql));
                #endif

                //set variables used in nLog.config
                NLog.LogManager.Configuration.Variables["connectionString"] = sql;
                NLog.LogManager.Configuration.Variables["appName"] = "CESMII-ProfileDesigner";

                // Add services to the container.
                ConfigureServices(builder.Services, builder.Configuration);

                builder.Services.AddEndpointsApiExplorer(); // needed for Swagger
                builder.Services.AddSwaggerGen();           // adds swagger generation

                var app = builder.Build();

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment())
                {
                    // JSON endpoint
                    app.UseSwagger();

                    // Swagger UI page
                    app.UseSwaggerUI(options =>
                    {
                        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CESMII.ProfileDesigner.Api v1");
                        options.RoutePrefix = "swagger"; // URL will be /swagger
                    });
                }

                app.UseHttpsRedirection();

                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.UseRouting();

                // Enable CORS. - this needs to go after UseRouting.
                app.UseCors(_corsPolicyName);

                // Enable authentications (Jwt in our case)
                app.UseAuthentication();

                app.UseMiddleware<UserAzureADMapping>();

                app.UseAuthorization();

                app.MapControllers();

                app.MapFallbackToFile("/index.html");

                app.Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Stopped program because of an exception");
                throw;
            }
            finally
            {
                LogManager.Shutdown();
            }

            // WARNING: DO NOT SET THIS FLAG!
            // With this flag, Npqsql will interpret DateTime.Kind Utc as local time, resulting in nasty publicationdate mismatches. This is one of the reasons this behavior was deprecated in Npgsql.
            // If you encounter an error writing a DateTime that is not Utc to the dataase, adjust to Utc before writing.
            //System.AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);  // Accept Postgres 'only UTC' rule.
            // END WARNING
            //CreateHostBuilder(args).Build().Run();
        }

        private static void ConfigureServices(IServiceCollection services, ConfigurationManager configuration)
        {
            services.Configure<UACloudLibClient.Options>(configuration.GetSection("CloudLibrary"));

            //profile and related data
            services.AddScoped<IRepository<ProfileTypeDefinition>, BaseRepo<ProfileTypeDefinition, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<ProfileAttribute>, BaseRepo<ProfileAttribute, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<ProfileInterface>, BaseRepo<ProfileInterface, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<LookupType>, BaseRepo<LookupType, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<EngineeringUnit>, BaseRepo<EngineeringUnit, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<EngineeringUnitRanked>, BaseRepo<EngineeringUnitRanked, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<LookupDataType>, BaseRepo<LookupDataType, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<LookupDataTypeRanked>, BaseRepo<LookupDataTypeRanked, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<LookupItem>, BaseRepo<LookupItem, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<ImportLog>, BaseRepo<ImportLog, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<ProfileTypeDefinitionAnalytic>, BaseRepo<ProfileTypeDefinitionAnalytic, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<ProfileTypeDefinitionFavorite>, BaseRepo<ProfileTypeDefinitionFavorite, ProfileDesignerPgContext>>();

            //profile type def related / stored proc repo
            services.AddScoped<IRepositoryStoredProcedure<ProfileTypeDefinitionSimple>, BaseRepoStoredProcedure<ProfileTypeDefinitionSimple, ProfileDesignerPgContext>>();
            services.AddScoped<IStoredProcedureDal<ProfileTypeDefinitionSimpleModel>, ProfileTypeDefinitionRelatedDAL>();

            //NodeSet Related Tables
            services.AddScoped<IRepository<Profile>, BaseRepo<Profile, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<NodeSetFile>, BaseRepo<NodeSetFile, ProfileDesignerPgContext>>();

            //stock tables
            services.AddScoped<IRepository<User>, BaseRepo<User, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<Organization>, BaseRepo<Organization, ProfileDesignerPgContext>>();
            services.AddScoped<IRepository<Permission>, BaseRepo<Permission, ProfileDesignerPgContext>>();

            // DAL objects
            services.AddScoped<UserDAL>();                  // Has extra methods outside of the IDal interface
            services.AddScoped<OrganizationDAL>();          // Has extra methods outside of the IDal interface
            services.AddScoped<ProfileTypeDefinitionDAL>(); // Has extra methods outside of the IDal interface

            services.AddScoped<IUserSignUpData, UserSignUpData>();

            //services.AddScoped<IDal<Organization,OrganizationModel>,OrganizationDAL>();
            services.AddScoped<IDal<ProfileTypeDefinition, ProfileTypeDefinitionModel>, ProfileTypeDefinitionDAL>();
            services.AddScoped<IDal<LookupItem, LookupItemModel>, LookupDAL>();
            services.AddScoped<IDal<LookupDataType, LookupDataTypeModel>, LookupDataTypeDAL>();
            services.AddScoped<IDal<LookupDataTypeRanked, LookupDataTypeRankedModel>, LookupDataTypeRankedDAL>();
            services.AddScoped<IDal<EngineeringUnit, EngineeringUnitModel>, EngineeringUnitDAL>();
            services.AddScoped<IDal<EngineeringUnitRanked, EngineeringUnitRankedModel>, LookupEngUnitRankedDAL>();
            services.AddScoped<IDal<LookupType, LookupTypeModel>, LookupTypeDAL>();
            services.AddScoped<IDal<ImportLog, ImportLogModel>, ImportLogDAL>();
            services.AddScoped<IDal<ProfileTypeDefinitionAnalytic, ProfileTypeDefinitionAnalyticModel>, ProfileTypeDefinitionAnalyticDAL>();

            //NodeSet related
            services.AddScoped<IDal<Profile, ProfileModel>, ProfileDAL>();
            services.AddScoped<IDal<NodeSetFile, NodeSetFileModel>, NodeSetFileDAL>();
            services.AddScoped<ICloudLibDal<CloudLibProfileModel>, CloudLibDAL>();
            services.AddScoped<ICloudLibWrapper, CloudLibWrapper>();
            services.AddCloudLibraryResolver();

            // Configuration, utils, one off objects
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<CESMII.ProfileDesigner.Common.ConfigUtil>();  // helper to allow us to bind to app settings data 
            services.AddScoped<DAL.Utils.ProfileMapperUtil>();  // helper to allow us to modify profile data for front end 
            services.AddScoped<Utils.CloudLibraryUtil>();  // helper to allow controllers to do stuff related to CloudLibPublish 
            services.AddScoped<Utils.ImportNotificationUtil>();  // helper to allow import service to send notification email
            services.AddScoped<ICustomRazorViewEngine, CustomRazorViewEngine>();  //this facilitates sending formatted emails w/o dependency on controller
            services.AddOpcUaImporter(configuration);

            services.AddScoped<SelfSignUpAuthFilter>();               // Validator for self-sign up - authentiate API Connector username & password.
            services.AddScoped<SelfServiceSignUpNotifyController>();  // API Connector for Self-Service Sign-Up User Flow
            services.AddSingleton<MailRelayService>();                   // helper for emailing (in CESMII.Common.SelfServiceSignUp)
            //services.AddSingleton<UACloudLibClient>(sp => new UACloudLibClient(configuration.GetSection("CloudLibrary")new UACloudLibClient.Options))

            services.AddControllers();

            //New - Azure AD approach replaces previous code above
            services.AddAuthentication("AzureAd")
                .AddMicrosoftIdentityWebApi(configuration, "AzureAdSettings", "AzureAd");

            //Revised since AAD implementation
            //Add permission authorization requirements.
            services.AddAuthorization(options =>
            {
                // this "permission" is set once AD user has a mapping to a user record in the Profile Designer DB
                options.AddPolicy(
                    nameof(PermissionEnum.UserAzureADMapped),
                    policy => policy.Requirements.Add(new PermissionRequirement(PermissionEnum.UserAzureADMapped)));
            });

#if DEBUG
            IdentityModelEventSource.ShowPII = true;
#endif
            services.AddCors(options =>
            {
                options.AddPolicy(_corsPolicyName,
                builder =>
                {
                    //TBD - uncomment, come back to this and lock down the origins based on the appsettings config settings
                    //Code Smell: builder.WithOrigins(configUtil.CorsSettings.AllowedOrigins);
                    builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                });
            });

            services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

            //Add support for background processing of long running import
            services.AddHostedService<LongRunningService>();
            services.AddSingleton<BackgroundWorkerQueue>();
            services.AddScoped<Utils.ImportService>();

            services.AddMvc(); //add this to permit emailing to bind models to view templates.

            services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
            });

            // Add in-memory caching
            services.AddMemoryCache();

            // Add response caching.
            services.AddResponseCaching();
        }

        //        public static IHostBuilder CreateHostBuilder(string[] args) =>
        //            Host.CreateDefaultBuilder(args)
        //                .ConfigureWebHostDefaults(webBuilder =>
        //                {
        //                    webBuilder.UseStartup<Startup>();
        //                })
        //                 .ConfigureLogging(logging =>
        //                 {
        //                     logging.ClearProviders();
        //                     logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        //#if DEBUG
        //                     logging.AddDebug();
        //#endif
        //                 })
        //                // Use NLog to provide ILogger instances.
        //                .UseNLog();
    }
}
