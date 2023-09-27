
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
| ShortName               | &check;  | Windows Certificate                          |
| Custom Capability       |          | Unchecked [ ]                             |
| Supported Job Types     | &check;  | Inventory,Add,Enrollment,Remove     |
| Needs Server            | &check;  | Checked [x]                         |
| Blueprint Allowed       |          | Unchecked [ ]                       |
| Uses PowerShell         |          | Unchecked [ ]                             |
| Requires Store Password |          | Unchecked [ ]                          |
| Supports Entry Password |          | Unchecked [ ]                         |
      
![k8sstlssecr_basic.png](docs%2Fscreenshots%2Fstore_types%2Fwindows certificate_basic.png)

##### UI Advanced Tab
| Field Name            | Required | Value                 |
|-----------------------|----------|-----------------------|
| Store Path Type       |          | undefined      |
| Supports Custom Alias |          | Forbidden |
| Private Key Handling  |          | Required  |
| PFX Password Style    |          | Default   |

![k8sstlssecr_advanced.png](docs%2Fscreenshots%2Fstore_types%2Fwindows certificate_advanced.png)

##### UI Custom Fields Tab
| Name           | Display Name         | Type   | Required | Default Value |
|----------------|----------------------|--------|----------|---------------|
| KubeNamespace  | Kube Namespace       | String |          | `default`   |
| KubeSecretName | Kube Secret Name     | String | &check;  |               |
| KubeSecretType | Kube Secret Type     | String | &check;  | `tls_secret`|


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
| ShortName               | &check;  | IIS Bound Certificate                          |
| Custom Capability       |          | Unchecked [ ]                             |
| Supported Job Types     | &check;  | Inventory,Add,Enrollment,Remove     |
| Needs Server            | &check;  | Checked [x]                         |
| Blueprint Allowed       |          | Unchecked [ ]                       |
| Uses PowerShell         |          | Unchecked [ ]                             |
| Requires Store Password |          | Unchecked [ ]                          |
| Supports Entry Password |          | Unchecked [ ]                         |
      
![k8sstlssecr_basic.png](docs%2Fscreenshots%2Fstore_types%2Fiis bound certificate_basic.png)

##### UI Advanced Tab
| Field Name            | Required | Value                 |
|-----------------------|----------|-----------------------|
| Store Path Type       |          | undefined      |
| Supports Custom Alias |          | Forbidden |
| Private Key Handling  |          | Required  |
| PFX Password Style    |          | Default   |

![k8sstlssecr_advanced.png](docs%2Fscreenshots%2Fstore_types%2Fiis bound certificate_advanced.png)

##### UI Custom Fields Tab
| Name           | Display Name         | Type   | Required | Default Value |
|----------------|----------------------|--------|----------|---------------|
| KubeNamespace  | Kube Namespace       | String |          | `default`   |
| KubeSecretName | Kube Secret Name     | String | &check;  |               |
| KubeSecretType | Kube Secret Type     | String | &check;  | `tls_secret`|

