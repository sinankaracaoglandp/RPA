using RPA.LicenseGenerator;

try
{
    var options = LicenseGenerationOptions.Parse(args);
    var document = LicenseGenerationService.Generate(options);

    // Yalnizca sir icermeyen ozet: parola/anahtar hicbir cikti yolunda yazilmaz.
    Console.WriteLine($"Lisans uretildi: {options.OutputPath}");
    Console.WriteLine($"  licenseId   : {document.Payload.LicenseId} (revision {document.Payload.Revision})");
    Console.WriteLine($"  customer    : {document.Payload.CustomerName} ({document.Payload.CustomerId})");
    Console.WriteLine($"  edition     : {document.Payload.Edition}");
    Console.WriteLine($"  installation: {document.Payload.InstallationId}");
    Console.WriteLine($"  maxAgents   : {document.Payload.MaxActivatedAgents}");
    Console.WriteLine($"  expiresAt   : {document.Payload.ExpiresAt:O}");
    return 0;
}
catch (LicenseGenerationException ex)
{
    Console.Error.WriteLine($"Hata: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(LicenseGenerationOptions.Usage);
    return 1;
}
