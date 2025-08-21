namespace CESMII.ProfileDesigner.Common
{
    using Microsoft.Extensions.Configuration;
    using CESMII.ProfileDesigner.Common.Models;

    public class ConfigUtil
    {
        private readonly IConfiguration _configuration;

        public ConfigUtil(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public GeneralConfig GeneralSettings
        {
            get
            {
                throw new System.Exception("Maybe Unused Code?");
                //var result = new GeneralConfig();
                //_configuration.GetSection("GeneralSettings").Bind(result);
                //return result;
            }
        }

        public PasswordConfig PasswordConfigSettings
        {
            get
            {
                throw new System.Exception("Maybe Unused Code?");
                //var result = new PasswordConfig();
                //_configuration.GetSection("PasswordSettings").Bind(result);
                //return result;
            }
        }

        public CorsConfig CorsSettings
        {
            get
            {
                throw new System.Exception("Maybe Unused Code?");
                //var result = new CorsConfig();
                //_configuration.GetSection("CorsSettings").Bind(result);
                //return result;
            }
        }

        public AuthenticationConfig AuthenticationSettings
        {
            get
            {
                throw new System.Exception("Maybe Unused Code?");
                //var result = new AuthenticationConfig();
                //_configuration.GetSection("AuthenticationSettings").Bind(result);
                //return result;
            }
        }
        
        public MailConfig MailSettings
        {
            get
            {
                var result = new MailConfig();
                _configuration.GetSection("MailSettings").Bind(result);
                return result;
            }
        }

        public JWTConfig JWTSettings
        {
            get
            {
                throw new System.Exception("Maybe Unused Code?");
                var result = new JWTConfig();
                _configuration.GetSection("JwtSettings").Bind(result);
                return result;
            }
        }

        public ProfilesConfig ProfilesSettings
        {
            get
            {
                var result = new ProfilesConfig();
                _configuration.GetSection("ProfileSettings").Bind(result);
                return result;
            }
        }

    }
}
