using CESMII.Common.CloudLibClient;
using CESMII.Common.SelfServiceSignUp;
using CESMII.Common.SelfServiceSignUp.Models;
using CESMII.Common.SelfServiceSignUp.Services;
using CESMII.OpcUa.NodeSetImporter;
using CESMII.ProfileDesigner.Api.Shared.Extensions;
using CESMII.ProfileDesigner.Api.Shared.Utils;
using CESMII.ProfileDesigner.Common;
using CESMII.ProfileDesigner.Common.Enums;
using CESMII.ProfileDesigner.Common.Utils;
using CESMII.ProfileDesigner.DAL;
using CESMII.ProfileDesigner.DAL.Models;
using CESMII.ProfileDesigner.Data.Contexts;
using CESMII.ProfileDesigner.Data.Entities;
using CESMII.ProfileDesigner.Data.Repositories;
using CESMII.ProfileDesigner.OpcUa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Microsoft.OpenApi.Models;
using Opc.Ua.Cloud.Library.Client;
using System;
using System.Threading.Tasks;

namespace CESMII.ProfileDesigner.Api
{
    public class Startup
    {
        private readonly string _corsPolicyName = "SiteCorsPolicy";
        private readonly string _version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var connectionStringProfileDesigner = Configuration.GetConnectionString("ProfileDesignerDB");
            //PostgreSql context

#if DEBUG
            services.AddDbContext<ProfileDesignerPgContext>(options =>
                    options.UseNpgsql(connectionStringProfileDesigner)
                    //options.UseNpgsql(connectionStringProfileDesigner, options => options.EnableRetryOnFailure())
                    .EnableSensitiveDataLogging());
#else
            services.AddDbContext<ProfileDesignerPgContext>(options =>
                    options.UseNpgsql(connectionStringProfileDesigner));
#endif

            //set variables used in nLog.config
            NLog.LogManager.Configuration.Variables["connectionString"] = connectionStringProfileDesigner;
            NLog.LogManager.Configuration.Variables["appName"] = "CESMII-ProfileDesigner";

            services.Configure<UACloudLibClient.Options>(Configuration.GetSection("CloudLibrary"));

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
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton<ConfigUtil>();  // helper to allow us to bind to app settings data 
            services.AddScoped<DAL.Utils.ProfileMapperUtil>();  // helper to allow us to modify profile data for front end 
            services.AddScoped<Utils.CloudLibraryUtil>();  // helper to allow controllers to do stuff related to CloudLibPublish 
            services.AddScoped<Utils.ImportNotificationUtil>();  // helper to allow import service to send notification email
            services.AddScoped<ICustomRazorViewEngine, CustomRazorViewEngine>();  //this facilitates sending formatted emails w/o dependency on controller
            services.AddOpcUaImporter(Configuration);

            services.AddScoped<SelfSignUpAuthFilter>();               // Validator for self-sign up - authentiate API Connector username & password.
            services.AddScoped<SelfServiceSignUpNotifyController>();  // API Connector for Self-Service Sign-Up User Flow
            services.AddSingleton<MailRelayService>();                   // helper for emailing (in CESMII.Common.SelfServiceSignUp)
            //services.AddSingleton<UACloudLibClient>(sp => new UACloudLibClient(configuration.GetSection("CloudLibrary")new UACloudLibClient.Options))

            services.AddControllers();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "CESMII.ProfileDesigner.Api",
                    Version = "v1"
                });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Name = "Authorization",
                    Description = "Please insert JWT with Bearer into field"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement {
                {
                    new OpenApiSecurityScheme {
                    Reference = new OpenApiReference {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                    },Array.Empty<string>()
                }
                });
            });

            //New - Azure AD approach replaces previous code above
            services.AddAuthentication("AzureAd")
                .AddMicrosoftIdentityWebApi(Configuration, "AzureAdSettings", "AzureAd");

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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Allow use of static files.
            app.UseStaticFiles();

            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(o => {
                    if (o is HttpContext ctx)
                    {
                        ctx.Response.Headers["x-api-version"] = _version;
                    }
                    return Task.CompletedTask;
                }, context);
                await next();
            });

            if (env.IsDevelopment() || env.IsEnvironment("Development.Local"))
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CESMII.ProfileDesigner.Api v1"));
            }
            else
            {
                app.UseHsts();
            }

            app.UseOpcUaImporter();
            app.UseHttpsRedirection();

            app.UseRouting();

            // Enable CORS. - this needs to go after UseRouting.
            app.UseCors(_corsPolicyName);

            // Enable authentications (Jwt in our case)
            app.UseAuthentication();

            app.UseMiddleware<UserAzureADMapping>();

            app.UseAuthorization();

            // https://docs.microsoft.com/en-us/aspnet/core/performance/caching/middleware?view=aspnetcore-2.2
            app.UseResponseCaching();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
