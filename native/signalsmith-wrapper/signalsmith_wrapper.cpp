#include <cstring>
#include <vector>
#include "signalsmith-stretch.h"

struct SsHandle {
    signalsmith::stretch::SignalsmithStretch<float> stretch;
    int channels;
    std::vector<std::vector<float>> inBufs;
    std::vector<std::vector<float>> outBufs;
    std::vector<const float*> inPtrs;
    std::vector<float*>       outPtrs;
};

extern "C" {

__declspec(dllexport) SsHandle* __cdecl ss_create(int channels, float sampleRate)
{
    SsHandle* h = new SsHandle();
    h->channels = channels;
    h->stretch.presetDefault(channels, sampleRate);
    h->inBufs.resize(channels);
    h->outBufs.resize(channels);
    h->inPtrs.resize(channels);
    h->outPtrs.resize(channels);
    return h;
}

__declspec(dllexport) void __cdecl ss_destroy(SsHandle* h)
{
    delete h;
}

__declspec(dllexport) void __cdecl ss_set_pitch(SsHandle* h, float semitones)
{
    h->stretch.setTransposeSemitones(semitones);
}

__declspec(dllexport) void __cdecl ss_process(SsHandle* h,
    const float* input, int inputSamples,
    float* output)
{
    int ch = h->channels;

    for (int c = 0; c < ch; c++) {
        h->inBufs[c].resize(inputSamples);
        h->outBufs[c].resize(inputSamples);
        for (int i = 0; i < inputSamples; i++)
            h->inBufs[c][i] = input[i * ch + c];
        h->inPtrs[c]  = h->inBufs[c].data();
        h->outPtrs[c] = h->outBufs[c].data();
    }

    h->stretch.process(h->inPtrs.data(), inputSamples,
                       h->outPtrs.data(), inputSamples);

    for (int i = 0; i < inputSamples; i++)
        for (int c = 0; c < ch; c++)
            output[i * ch + c] = h->outBufs[c][i];
}

__declspec(dllexport) int __cdecl ss_input_latency(SsHandle* h)
{
    return h->stretch.inputLatency();
}

} // extern "C"
