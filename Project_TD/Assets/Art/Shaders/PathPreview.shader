// 몬스터 경로 미리보기 (라운드 시작 전): 어두운 빨간 사각 레이저 + 2초마다 밝은 빨간 빛이 스폰에서 넥서스로 흐른다.
// 메시 UV.x = 그 경로의 스폰부터 잰 거리(칸). 정점 색 알파 = 부분별 불투명도 (스폰 표시 70%).
// _PulseTime은 스크립트(PathPreviewView.Tick)가 넘긴다 (게임 시간 기준, 셰이더에서 _Time을 쓰지 않음).
Shader "TowerDefence/PathPreview"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.42, 0.02, 0.03, 0.9)
        _PulseColor ("Pulse Color", Color) = (1.0, 0.35, 0.28, 1.0)
        _PulseTime ("Pulse Time (s)", Float) = 0
        _PulseSpeed ("Pulse Speed (cells/s)", Float) = 6
        _PulsePeriod ("Pulse Period (s)", Float) = 2
        _PulseLength ("Pulse Length (cells)", Float) = 3.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "PathPreview"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _PulseColor;
                float _PulseTime;
                float _PulseSpeed;
                float _PulsePeriod;
                float _PulseLength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 빛의 앞머리는 _PulsePeriod초마다 스폰(거리 0)에서 출발해 _PulseSpeed로 나아간다.
                // 앞머리가 지나간 직후가 가장 밝고, 뒤로 _PulseLength칸에 걸쳐 어두워진다 (우웅).
                float spacing = max(0.01, _PulseSpeed * _PulsePeriod);
                float behind = _PulseTime * _PulseSpeed - input.uv.x;
                float k = behind - floor(behind / spacing) * spacing;
                float glow = saturate(1.0 - k / max(0.01, _PulseLength));
                glow *= glow;
                half4 color = lerp(_BaseColor, _PulseColor, glow);
                color.a *= input.color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
