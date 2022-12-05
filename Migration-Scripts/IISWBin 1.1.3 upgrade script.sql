DECLARE @iiswbin INT = -1, @iiswibin2 INT = -1

SELECT @iiswbin = [StoreType] FROM [cms_agents].[CertStoreTypes] WHERE [ShortName] = 'IISWBin'
IF NOT @iiswbin = -1
BEGIN
    IF NOT EXISTS(SELECT [Id] FROM [cms_agents].[CertStoreTypeProperties] WHERE [StoreTypeId] = @iiswbin AND [Name] = 'WinRm Protocol')
    BEGIN
        INSERT INTO [cms_agents].[CertStoreTypeProperties]
                   ([StoreTypeId]
                   ,[Name]
                   ,[DisplayName]
                   ,[Type]
                   ,[Required]
                   ,[DependsOn]
                   ,[DefaultValue])
             VALUES
		           (@iiswbin,	'WinRm Protocol',	'WinRm Protocol',	2,	1, '', 'http,https')

    END
END

SELECT @iiswibin2 = [StoreType] FROM [cms_agents].[CertStoreTypes] WHERE [ShortName] = 'IISWBin'
IF NOT @iiswibin2 = -1
BEGIN
    IF NOT EXISTS(SELECT [Id] FROM [cms_agents].[CertStoreTypeProperties] WHERE [StoreTypeId] = @iiswibin2 AND [Name] = 'WinRm Port')
    BEGIN
        INSERT INTO [cms_agents].[CertStoreTypeProperties]
                   ([StoreTypeId]
                   ,[Name]
                   ,[DisplayName]
                   ,[Type]
                   ,[Required]
                   ,[DependsOn]
                   ,[DefaultValue])
             VALUES
		           (@iiswibin2,	'WinRm Port',	'WinRm Port',0,	1, '', '5985')
    END
END
