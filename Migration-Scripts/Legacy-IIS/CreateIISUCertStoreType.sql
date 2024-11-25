SET NOCOUNT ON

BEGIN TRY
	BEGIN TRANSACTION

	DECLARE @registration_index AS INT;
	SELECT @registration_index = MAX([ServerRegistration]) FROM [cms_agents].[CertStoreTypes];
	SET @registration_index = COALESCE(@registration_index + 1, 0);

	DECLARE @current_storetype_id AS INT;
	SELECT @current_storetype_id = IDENT_CURRENT('[cms_agents].[CertStoreTypes]') + 1;

	DECLARE @enrollment_job_id uniqueidentifier = NEWID();
	DECLARE @inventory_job_id uniqueidentifier = NEWID();
	DECLARE @management_job_id uniqueidentifier = NEWID();

	-- create enrollment job type
	INSERT INTO [cms_agents].[JobTypes] (
			 [Id]
			,[ConfigurationEndpoint]
			,[CompletionEndpoint]
			,[SubmitEndpoint]
			,[Name]
			,[Description]
			,[Capability]
		)
		VALUES
		(
			@enrollment_job_id, -- Id
			'AnyReenrollment/Configure',			-- ConfigurationEndpoint
			'AnyReenrollment/Complete',				-- CompletionEndpoint
			'AnyReenrollment/Submit',				-- SubmitEndpoint
			'IISUReenrollment',						-- Name
			'IIS Bound Certificate Reenrollment',	-- Description
			'CertStores.IISU.Reenrollment'		-- Capability
		);

	-- create inventory job type
	INSERT INTO [cms_agents].[JobTypes] (
			 [Id]
			,[ConfigurationEndpoint]
			,[CompletionEndpoint]
			,[SubmitEndpoint]
			,[Name]
			,[Description]
			,[Capability]
		)
		VALUES
		(
			@inventory_job_id,					-- Id
			'AnyInventory/Configure',			-- ConfigurationEndpoint
			'AnyInventory/Complete',			-- CompletionEndpoint
			NULL,								-- SubmitEndpoint
			'IISUInventory',					-- Name
			'IIS Bound Certificate Inventory',	-- Description
			'CertStores.IISU.Inventory'		    -- Capability
		);

	-- create management job type
	INSERT INTO [cms_agents].[JobTypes] (
			 [Id]
			,[ConfigurationEndpoint]
			,[CompletionEndpoint]
			,[SubmitEndpoint]
			,[Name]
			,[Description]
			,[Capability]
		)
		VALUES
		(
			@management_job_id,					-- Id
			'AnyManagement/Configure',			-- ConfigurationEndpoint
			'AnyManagement/Complete',			-- CompletionEndpoint
			NULL,								-- SubmitEndpoint
			'IISUManagement',				    -- Name
			'IIS Bound Certificate Management',	-- Description
			'CertStores.IISU.Management'		-- Capability
		);

	-- create IISU certificate store type
	INSERT INTO [cms_agents].[CertStoreTypes] (
			 [Name]
			,[ShortName]
			,[LocalStore]
			,[ServerRegistration]
			,[ImportType]
			,[InventoryJobType]
			,[ManagementJobType]
			,[AddSupported]
			,[RemoveSupported]
			,[CreateSupported]
			,[DiscoveryJobType]
			,[EnrollmentJobType]
			,[InventoryEndpoint]
			,[EntryPasswordSupported]
			,[StorePasswordRequired]
			,[PrivateKeyAllowed]
			,[StorePathType]
			,[CustomAliasAllowed]
			,[PowerShell]
			,[PasswordStyle]
			,[BlueprintAllowed]
		) 
		VALUES
		(
			'IIS Bound Certificate', -- Name
			'IISU', -- ShortName
			0, -- LocalStore
			@registration_index, -- ServerRegistration
			@current_storetype_id, -- ImportType
			@inventory_job_id, -- InventoryJobType
			@management_job_id, -- ManagementJobType
			1, -- AddSupported
			1, -- RemoveSupported
			0, -- CreateSupported
			NULL, -- DiscoveryJobType
			@enrollment_job_id, -- EnrollmentJobType
			'/AnyInventory/Update', -- InventoryEndpoint
			0, -- EntryPasswordSupported
			0, -- StorePasswordRequired
			2, -- PrivateKeyAllowed
			'["My","WebHosting"]', -- StorePathType
			0, -- CustomAliasAllowed
			0, -- PowerShell
			0, -- PasswordStyle
			0 -- BlueprintAllowed
		);

	-- create WinRm protocol property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'WinRm Protocol',	-- Name
			'WinRm Protocol',	-- DisplayName
			2,					-- Type
			1,					-- Required
			NULL,				-- DependsOn
			'https,http'		-- DefaultValue
		);

	-- create WinRm port property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'WinRm Port',	-- Name
			'WinRm Port',	-- DisplayName
			0,				-- Type
			1,				-- Required
			NULL,			-- DependsOn
			'5986'			-- DefaultValue
		);

	-- create SPN With Port protocol property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'spnwithport',		-- Name
			'SPN With Port',	-- DisplayName
			1,					-- Type
			0,					-- Required
			NULL,				-- DependsOn
			'false'				-- DefaultValue
		);

	-- create Server Username property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'ServerUsername',	-- Name
			'Server Username',	-- DisplayName
			3,					-- Type
			0,					-- Required
			NULL,				-- DependsOn
			NULL				-- DefaultValue
		);

	-- create Server Password property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'ServerPassword',	-- Name
			'Server Password',	-- DisplayName
			3,					-- Type
			0,					-- Required
			NULL,				-- DependsOn
			NULL				-- DefaultValue
		);

	-- create Use SSL property
	INSERT INTO [cms_agents].[CertStoreTypeProperties] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[Required]
			,[DependsOn]
			,[DefaultValue]
		)
		VALUES	
		(
			@current_storetype_id, -- StoreTypeId
			'ServerUseSsl', -- Name
			'Use SSL',		-- DisplayName
			1,				-- Type
			1,				-- Required
			NULL,			-- DependsOn
			'true'			-- DefaultValue
		);


	-- create IIS Site Name entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'SiteName',				-- Name
			'IIS Site Name',		-- DisplayName
			0,					    -- Type
			14,						-- RequiredWhen
			NULL,					-- DependsOn
			'Default Web Site',		-- DefaultValue
			NULL					-- Options
		);

	-- create IP Address entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'IPAddress',			-- Name
			'IP Address',			-- DisplayName
			0,					    -- Type
			14,						-- RequiredWhen
			NULL,					-- DependsOn
			'*',					-- DefaultValue
			NULL					-- Options
		);

	-- create Port entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'Port',					-- Name
			'Port',					-- DisplayName
			0,					    -- Type
			14,						-- RequiredWhen
			NULL,					-- DependsOn
			'443',					-- DefaultValue
			NULL					-- Options
		);

	-- create Host Name entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'HostName',				-- Name
			'Host Name',			-- DisplayName
			0,					    -- Type
			0,						-- RequiredWhen
			NULL,					-- DependsOn
			NULL,					-- DefaultValue
			NULL					-- Options
		);

	-- create SNI Support entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,	-- StoreTypeId
			'SniFlag',				-- Name
			'SSL Flags',			-- DisplayName
			0,						-- Type
			14,						-- RequiredWhen
			NULL,					-- DependsOn
			'0',					-- DefaultValue
			NULL					-- Options
		);

	-- create Protocol entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'Protocol',				-- Name
			'Protocol',				-- DisplayName
			2,					    -- Type
			14,						-- RequiredWhen
			NULL,					-- DependsOn
			'https',				-- DefaultValue
			'https'					-- Options
		);

	-- create Crypto Provider Name entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'ProviderName',		    -- Name
			'Crypto Provider Name', -- DisplayName
			0,					    -- Type
			0,						-- RequiredWhen
			NULL,					-- DependsOn
			NULL,					-- DefaultValue
			NULL					-- Options
		);

	-- create SAN entry parameter
	INSERT INTO [cms_agents].[CertStoreTypeEntryParameters] (
			 [StoreTypeId]
			,[Name]
			,[DisplayName]
			,[Type]
			,[RequiredWhen]
			,[DependsOn]
			,[DefaultValue]
			,[Options]
		)
		VALUES	
		(
			@current_storetype_id,  -- StoreTypeId
			'SAN',					-- Name
			'SAN',					-- DisplayName
			0,					    -- Type
			8,						-- RequiredWhen
			NULL,					-- DependsOn
			NULL,					-- DefaultValue
			NULL					-- Options
		);

	COMMIT TRANSACTION;
END TRY

BEGIN CATCH
	IF (@@TRANCOUNT > 0)
	BEGIN
		ROLLBACK TRANSACTION;
	END

	SELECT   
       ERROR_MESSAGE() AS ErrorMessage,  
       ERROR_SEVERITY() AS Severity,  
       ERROR_STATE() AS ErrorState; 
END CATCH

