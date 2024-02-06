-- REQUIREMENTS:
-- SQL Server 2016 or later

-- PREREQUISITES:
-- 1) The WinCert certificate store type must already be properly set up in Keyfactor Command. Run the CreateWinCertStoreType script to do so.
-- 2) Make sure to back up the targeted Keyfactor Command database before running this

-- *** BEGIN SET UP ***

-- *** STEP 1 OF 3: select the WinRM protocol that the Orchestrator should use. either 'https' or 'http'. ***
DECLARE @winrm_protocol NVARCHAR(5) = 'https';

-- *** STEP 2 OF 3: select the port that WinRM should use. ***
DECLARE @winrm_port INT = 5986;

-- *** STEP 3 OF 3: Select whether or not the -IncludePortInSPN flag will be set when WinRM creates the remote PowerShell connection. either 'true' or 'false' ***
DECLARE @spnwithport NVARCHAR(5) = 'false';

-- *** END SET UP ***


SET NOCOUNT ON

BEGIN TRY
	BEGIN TRANSACTION

	PRINT 'validating parameters...';

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
	
	DECLARE @upgrade_script NVARCHAR(MAX) = N'

	DECLARE @iis_store_data TABLE (
		[IISInventoryJobId] UNIQUEIDENTIFIER, 
		[ContainerId] INT,
		[ClientMachine] NVARCHAR(128),
		[StoreApproved] BIT,
		[AgentId] UNIQUEIDENTIFIER,
		[InventorySchedule] NVARCHAR(512),
		[InventoryEnabled] BIT,
		[WinInventoryJobId] UNIQUEIDENTIFIER,
		[WinStoreId] UNIQUEIDENTIFIER,
		[WinStoreContainerId] INT
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
	LEFT JOIN [cms_agents].[AgentSchedules] [as]
		ON [cs].[CertStoreInventoryJobId] = [as].[JobId]
	WHERE [cs].[CertStoreType] = @iis_store_type_id;

	DECLARE @win_inventory_job_type_id UNIQUEIDENTIFIER;
	SELECT @win_inventory_job_type_id = [Id] FROM [cms_agents].[JobTypes] WHERE [Name] = ''WinCertInventory'';

	DECLARE @win_cert_store_type_id INT;
	SELECT @win_cert_store_type_id = [StoreType] FROM [cms_agents].[CertStoreTypes] WHERE [ShortName] = ''WinCert'';


	-- create new certificate store containers if necessary
	INSERT INTO [cms_agents].[CertStoreContainers] (
		 [Name]
		,[Schedule]
		,[OverwriteSchedules]
		,[CertStoreType]
	)
	SELECT
		[csc].[Name] + '' - Upgraded WinCert'',
		[csc].[Schedule],
		[csc].[OverwriteSchedules],
		@win_cert_store_type_id
	FROM [cms_agents].[CertStoreContainers] [csc] WHERE [csc].CertStoreType = @iis_store_type_id;

	-- associate new certificate store containers with aggregated certificate store data
	UPDATE @iis_store_data
		SET [WinStoreContainerId] = [wincsc].[Id]
	FROM @iis_store_data [psd] 
		INNER JOIN [cms_agents].[CertStoreContainers] [csc] ON [psd].[ContainerId] = [csc].[Id]
		INNER JOIN [cms_agents].[CertStoreContainers] [wincsc] ON [wincsc].[Name] = [csc].[Name] + '' - Upgraded WinCert'';


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
		[psd].[WinInventoryJobId],
		[psd].[AgentId],
		@win_inventory_job_type_id,
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
		[psd].[WinInventoryJobId],
		''/AnyInventory/Update'',
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
		[psd].[WinStoreId],
		[psd].[WinInventoryJobId],
		[psd].[WinStoreContainerId],
		[psd].[ClientMachine],
		@store_path,
		@win_cert_store_type_id,
		[psd].[StoreApproved],
		0,
		''{"WinRm Protocol":"''+@winrm_protocol+''","WinRm Port":"''+CONVERT(NVARCHAR(10),@winrm_port)+''","spnwithport":"''+@spnwithport+''","ServerUsername":"''+CONVERT(NVARCHAR(36),NEWID())+''","ServerPassword":"''+CONVERT(NVARCHAR(36),NEWID())+''","ServerUseSsl":"true"}'',
		[psd].[AgentId]
	FROM @iis_store_data [psd];'

	EXEC dbo.sp_executesql @upgrade_script, 
		N'@iis_store_type_id INT,
		  @store_path NVARCHAR(256),
		  @winrm_protocol NVARCHAR(5),
		  @winrm_port INT,
		  @spnwithport NVARCHAR(5)',
		  4, 'Root', @winrm_protocol, @winrm_port, @spnwithport;

	EXEC dbo.sp_executesql @upgrade_script, 
		N'@iis_store_type_id INT,
		  @store_path NVARCHAR(256),
		  @winrm_protocol NVARCHAR(5),
		  @winrm_port INT,
		  @spnwithport NVARCHAR(5)',
		  8, 'Disallowed', @winrm_protocol, @winrm_port, @spnwithport;


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
