
### Windows Certificate Store Type
#### kfutil Create Windows Certificate Store Type
The following commands can be used with [kfutil](https://github.com/Keyfactor/kfutil). Please refer to the kfutil documentation for more information on how to use the tool to interact w/ Keyfactor Command.

```
bash
kfutil login
kfutil store - types create--name Windows Certificate 
```

#### UI Configuration
##### UI Basic Tab
| Field Name              | Required | Value                                     |
|-------------------------|----------|-------------------------------------------|
| Name                    | &check;  | Windows Certificate                          |
| ShortName               | &check;  | WinCert                          |
| Custom Capability       |          | Unchecked [ ]                             |
| Supported Job Types     | &check;  | Inventory,Add,Enrollment,Remove     |
| Needs Server            | &check;  | Checked [x]                         |
| Blueprint Allowed       |          | Unchecked [ ]                       |
| Uses PowerShell         |          | Unchecked [ ]                             |
| Requires Store Password |          | Unchecked [ ]                          |
| Supports Entry Password |          | Unchecked [ ]                         |
      
![wincert_basic.png](docs%2Fscreenshots%2Fstore_types%2Fwincert_basic.png)

##### UI Advanced Tab
| Field Name            | Required | Value                 |
|-----------------------|----------|-----------------------|
| Store Path Type       |          | Freeform      |
| Supports Custom Alias |          | Forbidden |
| Private Key Handling  |          | Required  |
| PFX Password Style    |          | Default   |

![wincert_advanced.png](docs%2Fscreenshots%2Fstore_types%2Fwincert_advanced.png)

##### UI Custom Fields Tab
| Name           | Display Name         | Type   | Required | Default Value |
| -------------- | -------------------- | ------ | -------- | ------------- |
|spnwithport|SPN With Port|Bool|false|false|
|WinRM Protocol|WinRM Protocol|MultipleChoice|https,http|true|
|WinRM Port|WinRM Port|String|5986|true|
|ServerUsername|Server Username|Secret|null|false|
|ServerPassword|Server Password|Secret|null|false|
|ServerUseSsl|Use SSL|Bool|true|true|


**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.

|Name|Display Name| Type|Default Value|Required When |
|----|------------|-----|-------------|--------------|
|ProviderName|Crypto Provider Name|String|||
|SAN|SAN|String||Reenrolling|


### IIS Bound Certificate Store Type
#### kfutil Create IIS Bound Certificate Store Type
The following commands can be used with [kfutil](https://github.com/Keyfactor/kfutil). Please refer to the kfutil documentation for more information on how to use the tool to interact w/ Keyfactor Command.

```
bash
kfutil login
kfutil store - types create--name IIS Bound Certificate 
```

#### UI Configuration
##### UI Basic Tab
| Field Name              | Required | Value                                     |
|-------------------------|----------|-------------------------------------------|
| Name                    | &check;  | IIS Bound Certificate                          |
| ShortName               | &check;  | IISU                          |
| Custom Capability       |          | Unchecked [ ]                             |
| Supported Job Types     | &check;  | Inventory,Add,Enrollment,Remove     |
| Needs Server            | &check;  | Checked [x]                         |
| Blueprint Allowed       |          | Unchecked [ ]                       |
| Uses PowerShell         |          | Unchecked [ ]                             |
| Requires Store Password |          | Unchecked [ ]                          |
| Supports Entry Password |          | Unchecked [ ]                         |
      
![iisu_basic.png](docs%2Fscreenshots%2Fstore_types%2Fiisu_basic.png)

##### UI Advanced Tab
| Field Name            | Required | Value                 |
|-----------------------|----------|-----------------------|
| Store Path Type       |          | ["My","WebHosting"]      |
| Supports Custom Alias |          | Forbidden |
| Private Key Handling  |          | Required  |
| PFX Password Style    |          | Default   |

![iisu_advanced.png](docs%2Fscreenshots%2Fstore_types%2Fiisu_advanced.png)

##### UI Custom Fields Tab
| Name           | Display Name         | Type   | Required | Default Value |
| -------------- | -------------------- | ------ | -------- | ------------- |
|spnwithport|SPN With Port|Bool|false|false|
|WinRm Protocol|WinRm Protocol|MultipleChoice|https,http|true|
|WinRm Port|WinRm Port|String|5986|true|
|ServerUsername|Server Username|Secret|null|false|
|ServerPassword|Server Password|Secret|null|false|
|ServerUseSsl|Use SSL|Bool|true|true|


**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.

|Name|Display Name| Type|Default Value|Required When |
|----|------------|-----|-------------|--------------|
|Port|Port|String|443||
|IPAddress|IP Address|String|*|Adding,Removing,Reenrolling|
|HostName|Host Name|String|||
|SiteName|IIS Site Name|String|Default Web Site|Adding,Removing,Reenrolling|
|SniFlag|SNI Support|MultipleChoice|0 - No SNI||
|Protocol|Protocol|MultipleChoice|https|Adding,Removing,Reenrolling|
|ProviderName|Crypto Provider Name|String|||
|SAN|SAN|String|||


### WinSql Store Type
#### kfutil Create WinSql Store Type
The following commands can be used with [kfutil](https://github.com/Keyfactor/kfutil). Please refer to the kfutil documentation for more information on how to use the tool to interact w/ Keyfactor Command.

```
bash
kfutil login
kfutil store - types create--name WinSql 
```

#### UI Configuration
##### UI Basic Tab
| Field Name              | Required | Value                                     |
|-------------------------|----------|-------------------------------------------|
| Name                    | &check;  | WinSql                          |
| ShortName               | &check;  | WinSql                          |
| Custom Capability       |          | Unchecked [ ]                             |
| Supported Job Types     | &check;  | Inventory,Add,Remove     |
| Needs Server            | &check;  | Checked [x]                         |
| Blueprint Allowed       |          | Checked [x]                       |
| Uses PowerShell         |          | Unchecked [ ]                             |
| Requires Store Password |          | Unchecked [ ]                          |
| Supports Entry Password |          | Unchecked [ ]                         |
      
![winsql_basic.png](docs%2Fscreenshots%2Fstore_types%2Fwinsql_basic.png)

##### UI Advanced Tab
| Field Name            | Required | Value                 |
|-----------------------|----------|-----------------------|
| Store Path Type       |          | My      |
| Supports Custom Alias |          | undefined |
| Private Key Handling  |          | Optional  |
| PFX Password Style    |          | Default   |

![winsql_advanced.png](docs%2Fscreenshots%2Fstore_types%2Fwinsql_advanced.png)

##### UI Custom Fields Tab
| Name           | Display Name         | Type   | Required | Default Value |
| -------------- | -------------------- | ------ | -------- | ------------- |
|WinRm Protocol|WinRm Protocol|MultipleChoice|https,http|true|
|WinRm Port|WinRm Port|String|5986|true|
|ServerUsername|Server Username|Secret|null|false|
|ServerPassword|Server Password|Secret|null|false|
|ServerUseSsl|Use SSL|Bool|true|true|
|RestartService|Restart SQL Service After Cert Installed|Bool|false|true|


**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.

|Name|Display Name| Type|Default Value|Required When |
|----|------------|-----|-------------|--------------|
|InstanceName|Instance Name|String|undefined||

