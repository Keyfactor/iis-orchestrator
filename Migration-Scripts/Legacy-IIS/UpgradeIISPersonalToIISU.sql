-- REQUIREMENTS:
-- SQL Server 2016 or later

-- PREREQUISITES:
-- 1) The IISU certificate store type must already be properly set up in Keyfactor Command. Run the CreateIISUCertStoreType script to do so.
-- 2) Make sure to back up the targeted Keyfactor Command database before running this

-- *** BEGIN SET UP ***

-- *** STEP 1 OF 5: Either enter '*' to migrate all IIS Personal stores, or enter a comma separated list of IDs of stores to convert.
--     Ex.: '25f80f46-fe0a-4ee6-b666-f601effd6847,22d0ca17-1739-4282-a85b-e59a3b431cdd' ***
DECLARE @comma_separated_store_ids NVARCHAR(MAX) = '*';

-- *** STEP 2 OF 5: select which Windows certificate store contains the IIS bound certificate. either 'My' for the personal store
--     or 'WebHosting' for the web hosting store. ***
DECLARE @store_path NVARCHAR(15) = 'My';

-- *** STEP 3 OF 5: select the WinRM protocol that the Orchestrator should use. either 'https' or 'http'. ***
DECLARE @winrm_protocol NVARCHAR(5) = 'https';

-- *** STEP 4 OF 5: select the port that WinRM should use. ***
DECLARE @winrm_port INT = 5986;

-- *** STEP 5 OF 5: Select whether or not the -IncludePortInSPN flag will be set when WinRM creates the remote PowerShell connection. either 'true' or 'false' ***
DECLARE @spnwithport NVARCHAR(5) = 'false';

-- *** END SET UP ***


SET NOCOUNT ON

BEGIN TRY
	BEGIN TRANSACTION
	
	DECLARE @store_ids_to_convert TABLE ([Id] UNIQUEIDENTIFIER);
	DECLARE @iis_store_type_id INT = 6;

	PRINT 'validating...';

	SELECT @comma_separated_store_ids = REPLACE(@comma_separated_store_ids, ' ', '');

	IF (@comma_separated_store_ids = '*')
	BEGIN
		INSERT INTO @store_ids_to_convert
			SELECT [Id] FROM [cms_agents].[CertStores] [cs]
			WHERE [cs].[CertStoreType] = @iis_store_type_id
	END
	ELSE
	BEGIN
		INSERT INTO @store_ids_to_convert
		SELECT value FROM STRING_SPLIT(@comma_separated_store_ids, ',');
		
		DECLARE @invalid_ids TABLE ([InvalidId] UNIQUEIDENTIFIER);

		INSERT INTO @invalid_ids
			SELECT [Id] FROM @store_ids_to_convert [si]
			WHERE [si].[Id] NOT IN 
			(
				SELECT [Id] FROM [cms_agents].[CertStores]
			);

		IF ((SELECT COUNT(*) FROM @invalid_ids) > 0)
		BEGIN
			PRINT 'One or more invalid IIS Personal store ids provided. See output table for list of invalid identifiers.';
			SELECT * FROM @invalid_ids;
			RETURN;
		END
	END

	IF (@store_path <> 'My' and @store_path <> 'WebHosting')
	BEGIN
		PRINT '@store_path must either be "My" or "WebHosting".';
		RETURN;
	END

	IF (@winrm_protocol <> 'https' and @winrm_protocol <> 'http')
	BEGIN
		PRINT '@winrm_protocol must either be "https" or "http".';
		RETURN;
	END

	IF (@spnwithport <> 'true' and @spnwithport <> 'false')
	BEGIN
		PRINT '@spnwithport must either be "true" or "false".';
		RETURN;
	END

	PRINT 'Validation Complete';

	DECLARE @iis_store_data TABLE (
		[IISInventoryJobId] UNIQUEIDENTIFIER, 
		[ContainerId] INT,
		[ClientMachine] NVARCHAR(128),
		[StoreApproved] BIT,
		[AgentId] UNIQUEIDENTIFIER,
		[InventorySchedule] NVARCHAR(512),
		[InventoryEnabled] BIT,
		[IISUInventoryJobId] UNIQUEIDENTIFIER,
		[IISUStoreId] UNIQUEIDENTIFIER,
		[IISUStoreContainerId] INT
	);

	-- aggregate necessary data for creating new certificate stores
	INSERT INTO @iis_store_data SELECT 
			[cs].[CertStoreInventoryJobId] AS [IISInventoryJobId],
			[cs].[ContainerId],
			[cs].[ClientMachine],
			[cs].[Approved] AS [StoreApproved],
			[cs].[AgentId],
			[as].[Schedule] AS [InventorySchedule],
			[as].[Enabled] AS [InventoryEnabled],
			CASE WHEN [cs].[CertStoreInventoryJobId] IS NOT NULL THEN NEWID() ELSE NULL END,
			NEWID(),
			NULL
	FROM [cms_agents].[CertStores] [cs]
	LEFT JOIN [cms_agents].[AgentSchedules] [as] ON [cs].[CertStoreInventoryJobId] = [as].[JobId]
	INNER JOIN @store_ids_to_convert [si] ON [cs].[Id] = [si].[Id];

	DECLARE @iisu_inventory_job_type_id UNIQUEIDENTIFIER;
	SELECT @iisu_inventory_job_type_id = [Id] FROM [cms_agents].[JobTypes] WHERE [Name] = 'IISUInventory';

	DECLARE @iisu_cert_store_type_id INT;
	SELECT @iisu_cert_store_type_id = [StoreType] FROM [cms_agents].[CertStoreTypes] WHERE [ShortName] = 'IISU';


	-- create new certificate store containers if necessary
	INSERT INTO [cms_agents].[CertStoreContainers] (
		 [Name]
		,[Schedule]
		,[OverwriteSchedules]
		,[CertStoreType]
	)
	SELECT
		[csc].[Name] + ' - Upgraded IISU',
		[csc].[Schedule],
		[csc].[OverwriteSchedules],
		@iisu_cert_store_type_id
	FROM [cms_agents].[CertStoreContainers] [csc] 
	WHERE [csc].[CertStoreType] = @iis_store_type_id
		AND [csc].[Id] NOT IN
		(
		-- make sure we are only upgrading containers that haven't been upgraded yet
			SELECT [csc].[Id] FROM [cms_agents].[CertStoreContainers] [csc2]
			WHERE [csc].[Name] + ' - Upgraded IISU' = [csc2].[Name]
		);

	-- associate new certificate store containers with aggregated certificate store data
	UPDATE @iis_store_data
		SET [IISUStoreContainerId] = [wincsc].[Id]
	FROM @iis_store_data [psd] 
		INNER JOIN [cms_agents].[CertStoreContainers] [csc] ON [psd].[ContainerId] = [csc].[Id]
		INNER JOIN [cms_agents].[CertStoreContainers] [wincsc] ON [wincsc].[Name] = [csc].[Name] + ' - Upgraded IISU';


	-- create new agent schedules
	INSERT INTO [cms_agents].[AgentSchedules] (
		 [JobId]
		,[AgentId]
		,[JobTypeId]
		,[Schedule]
		,[Enabled]
		,[RequestTimestamp]
		,[Retries]
	)
	SELECT
		[psd].[IISUInventoryJobId],
		[psd].[AgentId],
		@iisu_inventory_job_type_id,
		[psd].[InventorySchedule],
		[psd].[InventoryEnabled],
		NULL,
		0
	FROM @iis_store_data [psd]
		WHERE [psd].[IISInventoryJobId] IS NOT NULL;
			

	-- create new inventory jobs
	INSERT INTO [cms_agents].[CertStoreInventoryJobs] (
		 [JobId]
		,[InventoryEndpoint]
		,[RequestTimestamp]
	)
	SELECT
		[psd].[IISUInventoryJobId],
		'/AnyInventory/Update',
		CURRENT_TIMESTAMP
	FROM @iis_store_data [psd]
		WHERE [psd].[IISInventoryJobId] IS NOT NULL;


	-- create new certificate stores
	INSERT INTO [cms_agents].[CertStores] (
			 [Id]
			,[CertStoreInventoryJobId]
			,[ContainerId]
			,[ClientMachine]
			,[StorePath]
			,[CertStoreType]
			,[Approved]
			,[CreateIfMissing]
			,[Properties]
			,[AgentId]
	)
	SELECT 
		[psd].[IISUStoreId],
		[psd].[IISUInventoryJobId],
		[psd].[IISUStoreContainerId],
		[psd].[ClientMachine],
		@store_path,
		@iisu_cert_store_type_id,
		[psd].[StoreApproved],
		0,
		'{"WinRm Protocol":"'+@winrm_protocol+'","WinRm Port":"'+CONVERT(NVARCHAR(10),@winrm_port)+'","spnwithport":"'+@spnwithport+'","ServerUsername":"'+CONVERT(NVARCHAR(36),NEWID())+'","ServerPassword":"'+CONVERT(NVARCHAR(36),NEWID())+'","ServerUseSsl":"true"}',
		[psd].[AgentId]
	FROM @iis_store_data [psd];


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
