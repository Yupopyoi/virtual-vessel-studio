#include "sound_effects.h"

#include "dsp.h"
#include "mixer.h"

#include <cstring>

namespace vv::audio
{
    bool SoundEffects::Load(std::shared_ptr<const std::vector<float>> frames, int* effect)
    {
        for (int i = 0; i < MaxEffects; i++)
        {
            if (!_effects[i])
            {
                _effects[i] = std::move(frames);
                *effect = i;
                return true;
            }
        }

        return false;
    }

    std::shared_ptr<const std::vector<float>> SoundEffects::Unload(int effect)
    {
        if (effect < 0 || effect >= MaxEffects)
        {
            return nullptr;
        }

        for (Voice& voice : _voices)
        {
            if (voice.effect == effect)
            {
                voice = Voice{};
            }
        }

        return std::move(_effects[effect]);
    }

    bool SoundEffects::Play(int effect, float gainDb)
    {
        if (effect < 0 || effect >= MaxEffects || !_effects[effect])
        {
            return false;
        }

        int playing = 0;
        Voice* oldest = nullptr;
        Voice* free = nullptr;
        for (Voice& voice : _voices)
        {
            if (voice.effect < 0)
            {
                free = free == nullptr ? &voice : free;
                continue;
            }

            if (!voice.releasing)
            {
                playing++;
                oldest = oldest == nullptr || voice.started < oldest->started ? &voice : oldest;
            }
        }

        if (playing >= MaxVoices && oldest != nullptr)
        {
            oldest->releasing = true;
            _stolen++;
        }

        if (free == nullptr)
        {
            // Every place is taken, even those for voices fading out: this one is not played.
            return true;
        }

        free->effect = effect;
        free->position = 0;
        free->gain = DbToLinear(ClampGainDb(gainDb));
        free->releasing = false;
        free->started = ++_plays;
        return true;
    }

    void SoundEffects::StopAll()
    {
        for (Voice& voice : _voices)
        {
            if (voice.effect >= 0)
            {
                voice.releasing = true;
            }
        }
    }

    bool SoundEffects::ReadBlock(float* block)
    {
        bool any = false;
        std::memset(block, 0, static_cast<size_t>(BlockFrames) * Channels * sizeof(float));
        for (Voice& voice : _voices)
        {
            if (voice.effect < 0)
            {
                continue;
            }

            const std::vector<float>& frames = *_effects[voice.effect];
            size_t length = frames.size() / Channels;
            size_t count = length - voice.position < static_cast<size_t>(BlockFrames) ? length - voice.position : static_cast<size_t>(BlockFrames);
            for (size_t i = 0; i < count; i++)
            {
                // A voice that gave its place fades to silence over this block, so it does not click.
                float gain = voice.releasing ? voice.gain * (1.0f - (static_cast<float>(i) / BlockFrames)) : voice.gain;
                block[i * Channels] += frames[(voice.position + i) * Channels] * gain;
                block[(i * Channels) + 1] += frames[((voice.position + i) * Channels) + 1] * gain;
            }

            voice.position += count;
            any = any || count > 0;
            if (voice.releasing || voice.position >= length)
            {
                voice = Voice{};
            }
        }

        return any;
    }
}
