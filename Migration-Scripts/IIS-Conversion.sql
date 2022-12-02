-- REQUIREMENTS:
-- SQL Server 2016 or later

-- PREREQUISITES:
-- 1) The certificate store types you are attempting to convert TO need to already be properly set up in Keyfactor Command
-- 2) Make sure to back up the targeted Keyfactor Command database before running this

-- *** BEGIN SET UP ***

-- *** STEP 1 OF 4: set store type short name of the stores you wish to convert. ***
declare @IISBinShortName varchar(50) = 'IISWBin' -- By convention usually 'IISWBin'

-- *** STEP 2 OF 4: set the corresponding short name of the store type you wish to convert stores to
declare @IISUShortName varchar(50) = 'IISU' -- By convention usually 'IISWBin'.

-- *** STEP 3 OF 4: set whether you want to update the client's database permanently or just do a test run where results will be displayed but the database will NOT be updated
declare @RunInTestMode bit = 0  -- 0=database will be updated, 1=test run, client database will NOT be updated

-- *** STEP 4 OF 4: set whether you want to remove the IISWBin certificate store type after converting
declare @RemoveIISWBin bit = 1  -- 0=Do Not Remove, 1=Remove

-- *** END SET UP ***



declare @RFPEMStoreTypeId int

declare @StoreTypesToConvert TABLE(ToStoreShortName varchar(50), FromCertStoreTypeId int, ToCertStoreTypeId int, 
								   FromServerRegistration int, ToServerRegistration int, 
								   FromInventoryJobType uniqueidentifier, ToInventoryJobType uniqueidentifier,
								   FromManagementJobType uniqueidentifier, ToManagementJobType uniqueidentifier)

-- validate input
print 'validating...'
if (@IISBinShortName <> '' and not exists (select StoreType from cms_agents.CertStoreTypes where ShortName = @IISBinShortName)) 
begin 
	select 'IISWBin store type not found'; 
	return; 
end 
if (@IISUShortName <> '' and not exists (select StoreType from cms_agents.CertStoreTypes where ShortName = @IISUShortName)) 
begin 
	select 'IISU certificate store type not found'; 
	return; 
end 


-- Set up store type temp table
print 'get stores to convert'
insert into @StoreTypesToConvert 
(ToStoreShortName, FromCertStoretypeId, ToCertStoreTypeId, FromServerRegistration, ToServerRegistration,
 FromInventoryJobType, ToInventoryJobType, FromManagementJobType, ToManagementJobType)
select b.ShortName, a.StoreType, b.StoreType, a.ServerRegistration, b.ServerRegistration,
	a.InventoryJobType, b.InventoryJobType, a.ManagementJobType, b.ManagementJobType
from cms_agents.CertStoreTypes a
inner join cms_agents.CertStoreTypes b on 1=1
where (a.ShortName = @IISBinShortName and b.ShortName = @IISUShortName)



begin try
begin transaction
	
	print 'convert containers'
	-- convert certificate store containers
	update a
	set CertStoreType = b.ToCertStoreTypeId
	from cms_agents.CertStoreContainers a
	inner join @StoreTypesToConvert b on a.CertStoreType = b.FromCertStoreTypeId

	print 'convert certificate stores'
	-- convert certificate stores
	update a
	set a.CertStoreType = b.ToCertStoreTypeId
	from cms_agents.CertStores a
	inner join @StoreTypesToConvert b on a.CertStoreType = b.FromCertStoreTypeId

	print 'convert certificate store servers'
	-- convert certificate store servers
	update a
	set a.ServerType = b.ToServerRegistration
	from cms_agents.CertStoreServers a
	inner join @StoreTypesToConvert b on a.ServerType = b.FromServerRegistration
	where not exists (select * from cms_agents.CertStoreServers c where c.Name = a.Name and c.ServerType = b.ToServerRegistration)

	print 'convert existing entry parameters'
	update a
	set a.StoreTypeId = b.ToCertStoreTypeId
	from cms_agents.CertStoreTypeEntryParameters a
	inner join @StoreTypesToConvert b on a.StoreTypeId = b.FromCertStoreTypeId

	print 'Convert job schedules'
	update a
	set a.JobTypeId = b.ToJobType
	from cms_agents.AgentSchedules a
	inner join (select FromInventoryJobType FromJobType, ToInventoryJobType ToJobType
				from @StoreTypesToConvert
				union all
				select FromManagementJobType FromJobType, ToManagementJobType ToJobType
				from @StoreTypesToConvert
				) b on a.JobTypeId = b.FromJobType

	print 'Convert agent capabilities'
	update a
	set a.JobTypeId = b.ToJobType
	from cms_agents.AgentCapabilities a
	inner join (select FromInventoryJobType FromJobType, ToInventoryJobType ToJobType
				from @StoreTypesToConvert
				union all
				select FromManagementJobType FromJobType, ToManagementJobType ToJobType
				from @StoreTypesToConvert
				) b on a.JobTypeId = b.FromJobType

	print 'Convert blueprint jobs'
	update a
	set a.JobType = b.ToJobType
	from cms_agents.AgentBlueprintJobs a
	inner join (select FromInventoryJobType FromJobType, ToInventoryJobType ToJobType
				from @StoreTypesToConvert
				union all
				select FromManagementJobType FromJobType, ToManagementJobType ToJobType
				from @StoreTypesToConvert
				) b on a.JobType = b.FromJobType

	if (@RemoveIISWBin = 1)
	begin
		print 'Remove old IISWBin store type entry parameters'
		delete a
		from cms_agents.CertStoreTypeEntryParameters a
		inner join @StoreTypesToConvert b on a.StoreTypeId = b.FromCertStoreTypeId

		print 'Remove old IISWBin store type custom properties'
		delete a
		from cms_agents.CertStoreTypeProperties a
		inner join @StoreTypesToConvert b on a.StoreTypeId = b.FromCertStoreTypeId

		print 'Remove old IISWBin store type'
		delete a
		from cms_agents.CertStoreTypes a
		inner join @StoreTypesToConvert b on a.StoreType = b.FromCertStoreTypeId
	end

	select *
	from cms_agents.CertStoreTypes

	select *
	from cms_agents.CertStoreTypeProperties

	select *
	from cms_agents.CertStoreTypeEntryParameters

	select *
	from cms_agents.certstores

	select *
	from cms_agents.CertStoreServers

	select *
	from cms_agents.CertStoreContainers

	select *
	from cms_agents.AgentSchedules

	if (@RunInTestMode = 1)
	begin
            rollback tran;
	end
	else
	begin
	    commit tran;
	end

end try

begin catch
	if @@TRANCOUNT > 0
		rollback tran;

    SELECT   
       ERROR_MESSAGE(),  
       ERROR_SEVERITY(),  
       ERROR_STATE(); 
end catch
