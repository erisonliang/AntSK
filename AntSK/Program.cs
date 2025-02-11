using AntDesign.ProLayout;
using Microsoft.AspNetCore.Components;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using AntSK.Domain.Utils;
using AntSK.Services;
using AntSK.Domain.Common.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using AntSK.Domain.Options;
using Microsoft.KernelMemory.ContentStorage.DevTools;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.KernelMemory.Postgres;
using AntSK.Domain.Repositories;
using Microsoft.SemanticKernel.Plugins.Core;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(config =>
{
    //此设定解决JsonResult中文被编码的问题
    config.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    config.JsonSerializerOptions.Converters.Add(new DateTimeConverter());
    config.JsonSerializerOptions.Converters.Add(new DateTimeNullableConvert());
});
// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddAntDesign();
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetService<NavigationManager>()!.BaseUri)
});
builder.Services.Configure<ProSettings>(builder.Configuration.GetSection("ProSettings"));
builder.Services.AddScoped<IChartService, ChartService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddServicesFromAssemblies("AntSK.Domain");
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AntSK.Api", Version = "v1" });
    //添加Api层注释（true表示显示控制器注释）
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath, true);
    //添加Domain层注释（true表示显示控制器注释）
    var xmlFile1 = $"{Assembly.GetExecutingAssembly().GetName().Name.Replace("Api", "Domain")}.xml";
    var xmlPath1 = Path.Combine(AppContext.BaseDirectory, xmlFile1);
    c.IncludeXmlComments(xmlPath1, true);
    c.DocInclusionPredicate((docName, apiDes) =>
    {
        if (!apiDes.TryGetMethodInfo(out MethodInfo method))
            return false;
        var version = method.DeclaringType.GetCustomAttributes(true).OfType<ApiExplorerSettingsAttribute>().Select(m => m.GroupName);
        if (docName == "v1" && !version.Any())
            return true;
        var actionVersion = method.GetCustomAttributes(true).OfType<ApiExplorerSettingsAttribute>().Select(m => m.GroupName);
        if (actionVersion.Any())
            return actionVersion.Any(v => v == docName);
        return version.Any(v => v == docName);
    });
});

// 读取连接字符串配置
{
    builder.Configuration.GetSection("ConnectionStrings").Get<ConnectionOption>();
    builder.Configuration.GetSection("OpenAIOption").Get<OpenAIOption>();
}
InitSK(builder);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

InitDB(app);

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.UseSwagger();
//配置Swagger UI
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AntSK API"); //注意中间段v1要和上面SwaggerDoc定义的名字保持一致
});
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});
app.Run();
void InitDB(WebApplication app) 
{
    using (var scope = app.Services.CreateScope())
    {
        //codefirst 创建表
        var _repository = scope.ServiceProvider.GetRequiredService<IApps_Repositories>();
        _repository.GetDB().DbMaintenance.CreateDatabase();
        _repository.GetDB().CodeFirst.InitTables(typeof(Apps));
        _repository.GetDB().CodeFirst.InitTables(typeof(Kmss));
        _repository.GetDB().CodeFirst.InitTables(typeof(KmsDetails));
    }
}

//初始化SK
void InitSK(WebApplicationBuilder builder)
{
    var services = builder.Services;
    var handler = new OpenAIHttpClientHandler();
    services.AddScoped<Kernel>((serviceProvider) =>
    {
        var kernel = Kernel.CreateBuilder()
         .AddOpenAIChatCompletion(
          modelId: OpenAIOption.Model,
          apiKey: OpenAIOption.Key,
          httpClient: new HttpClient(handler))
          .Build();
        RegisterPluginsWithKernel(kernel);
        return kernel;
    });
    //Kernel Memory
    var searchClientConfig = new SearchClientConfig
    {
        MaxAskPromptSize = 128000,
        MaxMatchesCount = 3,
        AnswerTokens = 1000,
        EmptyAnswer = "知识库未搜索到相关内容"
    };

    var postgresConfig = builder.Configuration.GetSection("Postgres").Get<PostgresConfig>()!;
    services.AddScoped<MemoryServerless>(serviceProvider =>
    {
        var memory = new KernelMemoryBuilder()
           .WithPostgresMemoryDb(postgresConfig)
           .WithSimpleFileStorage(new SimpleFileStorageConfig { StorageType = FileSystemTypes.Volatile, Directory = "_files" })
           .WithSearchClientConfig(searchClientConfig)
           .WithOpenAITextGeneration(new OpenAIConfig()
           {
               APIKey = OpenAIOption.Key,
               TextModel = OpenAIOption.Model

           }, null, new HttpClient(handler))
           .WithOpenAITextEmbeddingGeneration(new OpenAIConfig()
           {
               APIKey = OpenAIOption.Key,
               EmbeddingModel = OpenAIOption.EmbeddingModel

           }, null, false, new HttpClient(handler))
           .Build<MemoryServerless>();
        return memory;
    });
}
 void RegisterPluginsWithKernel(Kernel kernel)
{
    kernel.ImportPluginFromObject(new ConversationSummaryPlugin(), "ConversationSummaryPlugin");
    kernel.ImportPluginFromObject(new TimePlugin(), "TimePlugin");
}

