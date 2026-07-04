namespace RPA.Infrastructure.Tests;

using System.Data;
using Moq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.Excel;
using RPA.Infrastructure.Activities.Csv;

/// <summary>
/// Excel ve CSV aktiviteleri testleri (Task 2.7.1).
/// TDD: read/write roundtrip, DataTable dönüşümü, veri bütünlüğü.
/// Spec Bölüm 5.3.
/// </summary>
public class ExcelCsvActivitiesTests
{
    // ====== Test Utilities ======

    private static IActivityExecutionContext CreateMockContext(Dictionary<string, object?> variables)
    {
        var mock = new Mock<IActivityExecutionContext>();
        var store = new Dictionary<string, object?>(variables);

        mock.Setup(c => c.GetVariable<T>(It.IsAny<string>()))
            .Returns<string>(name =>
            {
                if (store.TryGetValue(name, out var val))
                    return (T?)(val ?? default!);
                return default!;
            });

        mock.Setup(c => c.SetVariable(It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<string, object?>((name, val) => store[name] = val);

        mock.Setup(c => c.Log(It.IsAny<string>(), It.IsAny<LogLevel>()));

        mock.Setup(c => c.GetCredentialAsync(It.IsAny<string>()))
            .ReturnsAsync("mock-secret");

        mock.Setup(c => c.GetAssetAsync(It.IsAny<string>()))
            .ReturnsAsync("mock-asset");

        mock.Setup(c => c.TimeZone).Returns("UTC");
        mock.Setup(c => c.JobRunId).Returns(Guid.NewGuid());

        return mock.Object;
    }

    private static DataTable CreateSampleDataTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("ID", typeof(int));
        dt.Columns.Add("Name", typeof(string));
        dt.Columns.Add("Value", typeof(decimal));

        dt.Rows.Add(1, "Item1", 100.50m);
        dt.Rows.Add(2, "Item2", 200.75m);
        dt.Rows.Add(3, "Item3", 300.25m);

        return dt;
    }

    // ====== Excel Tests ======

    [Fact]
    public async Task ExcelWriteAndRead_Roundtrip_PreservesData()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
        var originalData = CreateSampleDataTable();

        try
        {
            // Act: Yaz
            var writeActivity = new ExcelWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", "TestSheet" },
                { "data", originalData },
                { "startCell", "A1" }
            });

            var writeResult = await writeActivity.ExecuteAsync(writeContext);
            Assert.True((bool)(writeResult["success"] ?? false));
            Assert.True(File.Exists(tempFile), "Excel dosyası oluşturulmalı");

            // Act: Oku
            var readActivity = new ExcelReadActivity();
            var readContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", "TestSheet" },
                { "range", "" }
            });

            var readResult = await readActivity.ExecuteAsync(readContext);
            var readData = readResult["data"] as DataTable;

            // Assert
            Assert.NotNull(readData);
            Assert.Equal(originalData.Columns.Count, readData!.Columns.Count);
            Assert.Equal(originalData.Rows.Count, readData.Rows.Count);

            for (int r = 0; r < originalData.Rows.Count; r++)
            {
                for (int c = 0; c < originalData.Columns.Count; c++)
                {
                    Assert.Equal(originalData.Rows[r][c], readData.Rows[r][c]);
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExcelWrite_EmptyDataTable_ThrowsArgumentException()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
        var emptyData = new DataTable();
        emptyData.Columns.Add("Col1");

        try
        {
            var writeActivity = new ExcelWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", "TestSheet" },
                { "data", emptyData },
                { "startCell", "A1" }
            });

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => writeActivity.ExecuteAsync(writeContext));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExcelRead_NonexistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var readActivity = new ExcelReadActivity();
        var readContext = CreateMockContext(new()
        {
            { "filePath", "/nonexistent/path/file.xlsx" },
            { "sheet", "" },
            { "range", "" }
        });

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => readActivity.ExecuteAsync(readContext));
    }

    [Fact]
    public async Task ExcelRead_DefaultFirstSheet_ReadsSuccessfully()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
        var originalData = CreateSampleDataTable();

        try
        {
            // Setup: Yaz
            var writeActivity = new ExcelWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", "Sheet1" },
                { "data", originalData },
                { "startCell", "A1" }
            });
            await writeActivity.ExecuteAsync(writeContext);

            // Act: Oku (sheet belirtilmeden)
            var readActivity = new ExcelReadActivity();
            var readContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", null },
                { "range", null }
            });

            var readResult = await readActivity.ExecuteAsync(readContext);
            var readData = readResult["data"] as DataTable;

            // Assert
            Assert.NotNull(readData);
            Assert.True(readData!.Rows.Count > 0);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExcelWrite_CreateNewFileIfNotExists()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_new_{Guid.NewGuid()}.xlsx");
        var data = CreateSampleDataTable();

        try
        {
            Assert.False(File.Exists(tempFile), "Dosya başta mevcut olmamalı");

            // Act
            var writeActivity = new ExcelWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "sheet", "NewSheet" },
                { "data", data },
                { "startCell", "A1" }
            });

            var result = await writeActivity.ExecuteAsync(writeContext);

            // Assert
            Assert.True((bool)(result["success"] ?? false));
            Assert.True(File.Exists(tempFile), "Dosya oluşturulmalı");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // ====== CSV Tests ======

    [Fact]
    public async Task CsvWriteAndRead_Roundtrip_PreservesData()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        var originalData = CreateSampleDataTable();

        try
        {
            // Act: Yaz
            var writeActivity = new CsvWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "data", originalData },
                { "delimiter", "," }
            });

            var writeResult = await writeActivity.ExecuteAsync(writeContext);
            Assert.True((bool)(writeResult["success"] ?? false));
            Assert.True(File.Exists(tempFile), "CSV dosyası oluşturulmalı");

            // Act: Oku
            var readActivity = new CsvReadActivity();
            var readContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "delimiter", "," }
            });

            var readResult = await readActivity.ExecuteAsync(readContext);
            var readData = readResult["data"] as DataTable;

            // Assert
            Assert.NotNull(readData);
            Assert.Equal(originalData.Columns.Count, readData!.Columns.Count);
            Assert.Equal(originalData.Rows.Count, readData.Rows.Count);

            // CSV okuyucu her değeri string olarak okur, bu yüzden string karşılaştırması yap
            for (int r = 0; r < originalData.Rows.Count; r++)
            {
                for (int c = 0; c < originalData.Columns.Count; c++)
                {
                    Assert.Equal(
                        originalData.Rows[r][c]?.ToString() ?? "",
                        readData.Rows[r][c]?.ToString() ?? "");
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CsvWrite_CustomDelimiter_WorksCorrectly()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        var data = CreateSampleDataTable();

        try
        {
            // Act: Yaz semicolon delimiter ile
            var writeActivity = new CsvWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "data", data },
                { "delimiter", ";" }
            });

            await writeActivity.ExecuteAsync(writeContext);

            // Assert: Dosya içeriğinde semicolon olması kontrol et
            var fileContent = File.ReadAllText(tempFile);
            Assert.Contains(";", fileContent);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CsvRead_CustomDelimiter_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");

        try
        {
            // Semicolon-delimited CSV yazma
            var lines = new[]
            {
                "ID;Name;Value",
                "1;Item1;100",
                "2;Item2;200"
            };
            File.WriteAllLines(tempFile, lines);

            // Act
            var readActivity = new CsvReadActivity();
            var readContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "delimiter", ";" }
            });

            var readResult = await readActivity.ExecuteAsync(readContext);
            var readData = readResult["data"] as DataTable;

            // Assert
            Assert.NotNull(readData);
            Assert.Equal(3, readData!.Columns.Count);
            Assert.Equal(2, readData.Rows.Count);
            Assert.Equal("Item1", readData.Rows[0]["Name"]);
            Assert.Equal("Item2", readData.Rows[1]["Name"]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CsvWrite_EmptyDataTable_ThrowsArgumentException()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        var emptyData = new DataTable();
        emptyData.Columns.Add("Col1");

        try
        {
            var writeActivity = new CsvWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "data", emptyData },
                { "delimiter", "," }
            });

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => writeActivity.ExecuteAsync(writeContext));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CsvRead_NonexistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var readActivity = new CsvReadActivity();
        var readContext = CreateMockContext(new()
        {
            { "filePath", "/nonexistent/path/file.csv" },
            { "delimiter", "," }
        });

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => readActivity.ExecuteAsync(readContext));
    }

    [Fact]
    public async Task CsvWrite_NullData_ThrowsArgumentException()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");

        try
        {
            var writeActivity = new CsvWriteActivity();
            var writeContext = CreateMockContext(new()
            {
                { "filePath", tempFile },
                { "data", null },
                { "delimiter", "," }
            });

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => writeActivity.ExecuteAsync(writeContext));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // ====== Metadata Tests ======

    [Fact]
    public void ExcelReadActivity_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var activity = new ExcelReadActivity();

        // Act
        var metadata = activity.GetMetadata();

        // Assert
        Assert.Equal("Excel.Read", metadata.ActivityId);
        Assert.Equal("Excel Oku", metadata.DisplayName);
        Assert.Equal("Excel", metadata.Category);
        Assert.Equal(3, metadata.Inputs.Count);
        Assert.Single(metadata.Outputs);
        Assert.Contains("excel", metadata.RequiredCapabilities);
    }

    [Fact]
    public void ExcelWriteActivity_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var activity = new ExcelWriteActivity();

        // Act
        var metadata = activity.GetMetadata();

        // Assert
        Assert.Equal("Excel.Write", metadata.ActivityId);
        Assert.Equal("Excel Yaz", metadata.DisplayName);
        Assert.Equal("Excel", metadata.Category);
        Assert.Equal(4, metadata.Inputs.Count);
        Assert.Single(metadata.Outputs);
        Assert.Equal("A1", metadata.Inputs.First(i => i.Name == "startCell").DefaultValue);
    }

    [Fact]
    public void CsvReadActivity_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var activity = new CsvReadActivity();

        // Act
        var metadata = activity.GetMetadata();

        // Assert
        Assert.Equal("Csv.Read", metadata.ActivityId);
        Assert.Equal("CSV Oku", metadata.DisplayName);
        Assert.Equal("CSV", metadata.Category);
        Assert.Equal(2, metadata.Inputs.Count);
        Assert.Single(metadata.Outputs);
        Assert.Equal(",", metadata.Inputs.First(i => i.Name == "delimiter").DefaultValue);
    }

    [Fact]
    public void CsvWriteActivity_GetMetadata_ReturnsCorrectMetadata()
    {
        // Arrange
        var activity = new CsvWriteActivity();

        // Act
        var metadata = activity.GetMetadata();

        // Assert
        Assert.Equal("Csv.Write", metadata.ActivityId);
        Assert.Equal("CSV Yaz", metadata.DisplayName);
        Assert.Equal("CSV", metadata.Category);
        Assert.Equal(3, metadata.Inputs.Count);
        Assert.Single(metadata.Outputs);
        Assert.Contains("csv", metadata.RequiredCapabilities);
    }
}
