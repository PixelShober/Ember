namespace Ember.Core.Tests;

/// <summary>llama-server without Ember: start script, Pi extension and the GPU check.</summary>
[Collection("language")]
public class LaunchTests
{
    [Fact]
    public void Gpu_use_shows_in_buffer_lines_of_newer_llama_versions()
    {
        Assert.Equal("CUDA0", LlamaCpp.Device("0.12.001.000 I load_tensors:        CUDA0 model buffer size = 15873.42 MiB"));
        Assert.Equal("CUDA0", LlamaCpp.Device("llama_kv_cache:      CUDA0 KV buffer size =  5120.00 MiB"));
        Assert.Null(LlamaCpp.Device("load_tensors:   CPU_Mapped model buffer size =   545.00 MiB"));
    }

    [Fact]
    public void Nvidia_smi_lists_the_processes_on_the_gpu()
    {
        Assert.Equal(new HashSet<int> { 12345, 777 }, Gpu.ParsePids("12345\r\n777\n\n"));
        Assert.Empty(Gpu.ParsePids("No running processes found"));
    }

    [Theory]
    [InlineData("-ngl", "-ngl")]
    [InlineData(@"C:\Ember\Models\Qwen 27B.gguf", "\"C:\\Ember\\Models\\Qwen 27B.gguf\"")]
    [InlineData("{\"reasoning_effort\":\"medium\"}", "\"{\\\"reasoning_effort\\\":\\\"medium\\\"}\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("100%", "100%%")]
    [InlineData("", "\"\"")]
    public void Script_arguments_survive_cmd_and_the_c_runtime(string arg, string quoted) =>
        Assert.Equal(quoted, LlamaLaunch.Quote(arg));

    [Fact]
    public void Start_script_runs_llama_server_with_embers_arguments()
    {
        var script = LlamaLaunch.Script(@"C:\Ember\llama.cpp\b9000-cuda\llama-server.exe", @"C:\Ember\llama.cpp\b9000-cuda",
                                        new[] { "-m", @"C:\Ember\Models\Qwen3.8-27B-UD-Q4_K_XL.gguf", "--port", "8080" }, 8080, "Qwen (Test)");
        Assert.StartsWith("@echo off\r\nchcp 65001", script);
        Assert.Contains(@"cd /d C:\Ember\llama.cpp\b9000-cuda", script);
        Assert.Contains(@"C:\Ember\llama.cpp\b9000-cuda\llama-server.exe -m C:\Ember\Models\Qwen3.8-27B-UD-Q4_K_XL.gguf --port 8080", script);
        Assert.Contains("http://127.0.0.1:8080/health", script);
        // Parentheses in the echoed model name would end the block early.
        Assert.Contains("Qwen ^(Test^)", script);
        Assert.DoesNotContain("\n", script.Replace("\r\n", ""));
    }

    [Fact]
    public void Pi_extension_gets_path_provider_and_texts()
    {
        var ts = LlamaLaunch.Extension();
        Assert.DoesNotContain("%CONFIG%", ts);
        Assert.DoesNotContain("%PROVIDER%", ts);
        Assert.DoesNotContain("%MESSAGES%", ts);
        Assert.Contains("String.raw`" + LlamaLaunch.ConfigFile + "`", ts);
        Assert.Contains("const PROVIDER = \"llama-cpp\";", ts);
        Assert.Contains("\"stateOff\":", ts);
    }
}
