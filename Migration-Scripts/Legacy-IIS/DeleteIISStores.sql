SET NOCOUNT ON

BEGIN TRY
	BEGIN TRANSACTION

	DECLARE @IISStoreTypeIds TABLE (Id int);
	DECLARE @IISJobTypeIds TABLE (Id uniqueidentifier);

	INSERT INTO @IISStoreTypeIds
	SELECT [StoreType]
	FROM [cms_agents].[CertStoreTypes]
	WHERE [ShortName] = 'IIS';

	INSERT INTO @IISJobTypeIds
	SELECT [Id]
	FROM [cms_agents].[JobTypes]
	WHERE [Capability] LIKE 'CertStores.IIS.%';

	DELETE [csc]
	FROM [cms_agents].[CertStoreCertificates] [csc]
		INNER JOIN [cms_agents].[CertStoreInventoryItems] [csii] ON [csii].[Id] = [csc].[CertStoreInventoryItemId]
		INNER JOIN [cms_agents].[CertStores] [cs] ON [csii].[CertStoreId] = [cs].[Id]
		INNER JOIN @IISStoreTypeIds [sti] ON [sti].[Id] = [cs].[CertStoreType];

	DELETE [csii]
	FROM [cms_agents].[CertStoreInventoryItems] [csii]
		INNER JOIN [cms_agents].[CertStores] [cs] ON [csii].[CertStoreId] = [cs].[Id]
		INNER JOIN @IISStoreTypeIds [sti] ON [sti].[Id] = [cs].[CertStoreType];

	DELETE [csmj]
	FROM [cms_agents].[CertStoreManagementJobs] [csmj]
		INNER JOIN [cms_agents].[AgentSchedules] [as] ON [csmj].[JobId] = [as].[JobId]
		INNER JOIN @IISJobTypeIds [jti] ON [jti].[Id] = [as].[JobTypeId];

	DELETE [csms]
	FROM [dbo].[CertStoreManagementStaging] [csms]
		INNER JOIN [cms_agents].[CertStores] [cs] ON [csms].[CertStoreId] = [cs].[Id]
		INNER JOIN @IISStoreTypeIds [sti] ON [sti].[Id] = [cs].[CertStoreType];

	DELETE [csmjs]
	FROM [cms_agents].[CertStoreManagementJobStaging] [csmjs]
		INNER JOIN [cms_agents].[CertStores] [cs] ON [csmjs].[StoreId] = [cs].[Id]
		INNER JOIN @IISStoreTypeIds [st] ON [st].[Id] = [cs].[CertStoreType];

	DELETE [csrj]
	FROM [cms_agents].[CertStoreReenrollmentJobs] [csrj]
		INNER JOIN [cms_agents].[AgentSchedules] [as] ON [csrj].[JobId] = [as].[JobId]
		INNER JOIN @IISJobTypeIds [jti] ON [jti].[Id] = [as].[JobTypeId];

	DELETE [cs]
	FROM [cms_agents].[CertStores] [cs]
		INNER JOIN @IISStoreTypeIds [st] ON [cs].[CertStoreType] = [st].[Id];

	DELETE [csc]
	FROM [cms_agents].[CertStoreContainers] [csc]
		INNER JOIN @IISStoreTypeIds [st] ON [csc].[CertStoreType] = [st].[Id];	

	DELETE [csij]
	FROM [cms_agents].[CertStoreInventoryJobs] [csij]
		INNER JOIN [cms_agents].[AgentSchedules] [as] ON [csij].[JobId] = [as].[JobId]
		INNER JOIN @IISJobTypeIds [jti] ON [jti].[Id] = [as].[JobTypeId];

	DELETE [as]
	FROM [cms_agents].[AgentSchedules] [as]
		INNER JOIN @IISJobTypeIds [jt] ON [jt].[Id] = [as].[JobTypeId];

	DELETE [cstp]
	FROM [cms_agents].[CertStoreTypeProperties] [cstp]
		INNER JOIN @IISStoreTypeIds [st] ON [cstp].[StoreTypeId] = [st].[Id];

	DELETE [cstep]
	FROM [cms_agents].[CertStoreTypeEntryParameters] [cstep]
		INNER JOIN @IISStoreTypeIds [st] ON [cstep].[StoreTypeId] = [st].[Id];

	DELETE [cst]
	FROM [cms_agents].[CertStoreTypes] [cst]
		INNER JOIN @IISStoreTypeIds [st] ON [cst].[StoreType] = [st].[id];

	DELETE [arg]
	FROM [cms_agents].[AgentRegistrationGroups] [arg]
		INNER JOIN @IISJobTypeIds [jt] ON [arg].[JobTypeId] = [jt].[Id];

	DELETE [ars]
	FROM [cms_agents].[AgentRegistrationSettings] [ars]
		INNER JOIN @IISJobTypeIds [jt] ON [ars].[JobTypeId] = [jt].[Id];

	DELETE [ac]
	FROM [cms_agents].[AgentCapabilities] [ac]
		INNER JOIN @IISJobTypeIds [jt] ON [ac].[JobTypeId] = [jt].[Id];

	DELETE [jtf]
	FROM [cms_agents].[JobTypeFields] [jtf]
		INNER JOIN @IISJobTypeIds [jt] ON [jtf].[JobTypeId] = [jt].[Id];

	DELETE [jt]
	FROM [cms_agents].[JobTypes] [jt]
		INNER JOIN @IISJobTypeIds [jti] ON [jt].[Id] = [jti].[Id];

	UPDATE [cms_agents].[CertStoreContainers]
		SET [Name] = REPLACE([Name],' - Upgraded IISU','')
	WHERE [Name] LIKE '% - Upgraded IISU';

	UPDATE [cms_agents].[CertStoreContainers]
		SET [Name] = REPLACE([Name],' - Upgraded WinCert','')
	WHERE [Name] LIKE '% - Upgraded WinCert';

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