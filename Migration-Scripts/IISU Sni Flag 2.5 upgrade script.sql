SET NOCOUNT ON

BEGIN TRY
    BEGIN TRANSACTION

    DECLARE @IISUShortName VARCHAR(50) = 'IISU'
    DECLARE @SniFlagParameter VARCHAR(50) = 'SniFlag'

    DECLARE @StoreTypeId INT

    -- get store type id
    SELECT @StoreTypeId = storetypes.[StoreType]
    FROM [cms_agents].[CertStoreTypes] AS storetypes
    WHERE @IISUShortName = storetypes.[ShortName]

    -- get list of cert stores guids of that type
    SELECT certstores.[Id]
    INTO #StoreGuids
    FROM [cms_agents].[CertStores] AS certstores
    WHERE @StoreTypeId = certstores.[CertStoreType]

    -- get list of certstoreinventoryitems matching on store guid
    SELECT inventory.[Id], inventory.[EntryParameters]
    INTO #InventoryItems
    FROM [cms_agents].[CertStoreInventoryItems] AS inventory
        INNER JOIN #StoreGuids ON #StoreGuids.[Id] = inventory.[CertStoreId]

    -- update entry parameters to new setting
    UPDATE [cms_agents].[CertStoreTypeEntryParameters]
    SET [DisplayName] = 'SSL Flags',
        [Type] = '0',
        [DefaultValue] = '0',
        [Options] = NULL
    WHERE [StoreTypeId] = @StoreTypeId
        AND [Name] = @SniFlagParameter

    -- perform batch processing on certstoreinventoryitems to alter their EntryParameters to change the SNiFlag value to be a simple character instead of lots of text
    -- replace 0 - No SNI
    UPDATE inventoryitems
    SET inventoryitems.[EntryParameters] = REPLACE(inventoryitems.[EntryParameters], '0 - No SNI', '0')
    FROM [cms_agents].[CertStoreInventoryItems] AS inventoryitems
        INNER JOIN #InventoryItems ON inventoryitems.[Id] = #InventoryItems.[Id]
    WHERE inventoryitems.[EntryParameters] LIKE '%0 - No SNI%'

    -- replace 1 - SNI Enabled
    UPDATE inventoryitems
    SET inventoryitems.[EntryParameters] = REPLACE(inventoryitems.[EntryParameters], '1 - SNI Enabled', '1')
    FROM [cms_agents].[CertStoreInventoryItems] AS inventoryitems
        INNER JOIN #InventoryItems ON inventoryitems.[Id] = #InventoryItems.[Id]
    WHERE inventoryitems.[EntryParameters] LIKE '%1 - SNI Enabled%'

    -- replace 2 - Non SNI Binding
    UPDATE inventoryitems
    SET inventoryitems.[EntryParameters] = REPLACE(inventoryitems.[EntryParameters], '2 - Non SNI Binding', '2')
    FROM [cms_agents].[CertStoreInventoryItems] AS inventoryitems
        INNER JOIN #InventoryItems ON inventoryitems.[Id] = #InventoryItems.[Id]
    WHERE inventoryitems.[EntryParameters] LIKE '%2 - Non SNI Binding%'

    -- replace 3 - SNI Binding
    UPDATE inventoryitems
    SET inventoryitems.[EntryParameters] = REPLACE(inventoryitems.[EntryParameters], '3 - SNI Binding', '3')
    FROM [cms_agents].[CertStoreInventoryItems] AS inventoryitems
        INNER JOIN #InventoryItems ON inventoryitems.[Id] = #InventoryItems.[Id]
    WHERE inventoryitems.[EntryParameters] LIKE '%3 - SNI Binding%'

    COMMIT TRANSACTION
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

