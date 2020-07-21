## Entity Framework Core

Because the Entity Framework model and context are in a netstandard classlib project the startup project needs to be specified.

##### Add migrations

    cd ./src/BikeDataProject.Statistics/
    dotnet ef migrations add InitialDb --startup-project ../BikeDataProject.Statistics.Service/BikeDataProject.Statistics.Service.csproj
    
##### Update database
    
    cd ./src/BikeDataProject.Statistics/
    dotnet ef database update --startup-project ../BikeDataProject.Statistics.Service/BikeDataProject.Statistics.Service.csproj  

