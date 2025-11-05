<h1>UA Nodeset Editor (previous name: Profile Designer)</h1>
<h2>Prerequisites</h2>
<ul>
<li>
Install node.js (version > 18.17) - https://nodejs.org/en/
</li>
<li>
Install npm (version > 8.5.4) - https://www.npmjs.com/ (npm install -g npm)
</li>
<li>
	React - https://reactjs.org/ - First time React users, install React using create-react-app from a node.js command prompt, a tool that installs all of the dependencies to build and run a full React.js application.
</li>
<li>
	.NET Core 6.0.x, Visual Studio 2022 or equivalent
</li>
<li>
	DB - Install PostgreSQL
	- Install PostgreSql, including pgAdmin (https://www.enterprisedb.com/downloads/postgres-postgresql-downloads)
	- See below for more details on setting up DB for this project. 
</li>
</ul>

<h2>Directories</h2>
<ul>
<li>
	\api - This contains a .NET web API back end for profile designer. Within this solution, the OPC translations will occur, database connections will occur, etc. 
</li>
<li>
	\frontend - This contains the REACT front end for profile designer.
</li>
<li>
	\SampleNodeSets - This contains nodesets that we use to import into system. Any OPC UA compliant nodeset is permitted. These are stored just for convenience while developing within the system.
</li>
<li>
	\sql - This contains the SQL script used to generate the DB structure and insert required lookup data as well as some sample users.  
</li>
</ul>

<h2>How to Build</h2>
<ol>
<li>
	Clone the repo from GIT.
</li>
<li>
	<b>Build/Run the front end: </b>
	<ul>
		<li>
			cd \frontend
		</li>
		<li>
			npm install
		</li>
		<li>
			npm run start 
		</li>
		<li>
			Verify the site is running in a browser: http://localhost:3000
		</li>
	</ul>
	<p>
		Note: The ENV files in the root React folder point to the base URL for the web API.
		Login: The login process was intentionally simple. Use cesmii/cesmii to login. 
	</p>
</li>
<li>
	<b>Build/Run the back end API (.NET 6 Solution): </b>
	<p>
		Standard .NET build and run. 
	</p>
</li>
<li>
	<b>PostgreSql DB </b>
	<p>
		See above for initial install instructions.
		- Run pgAdmin
		- Create local DB (see AppSettings.json for database name.)
		- Open the Query Tool (Tools menu)
		- Open the CESMII-Profile-Designer\sql\CESMII.ProfileDesigner.DB.sql file
		- Create the cesmii role/login and the database (comment out everything except the create role and create database sections and run the script)
		- Open a query tool on the newly created database and run the rest of the script
		- Change the password on the cesmii login to match the one in the appsettings.development.json file.
	</p>
</li>
</ol>
