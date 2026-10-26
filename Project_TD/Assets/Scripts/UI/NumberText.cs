using System;
using System.Runtime.InteropServices;
using UnityEngine.UI;

namespace TowerDefence
{
    // "캐시 105"처럼 숫자 하나가 바뀌는 HUD 글자를 GC 할당 없이 갱신한다 (GDD 8-5 웨이브 중 GC 0B).
    // uGUI Text는 string만 받으므로, 자릿수마다 미리 만든 문자열 버퍼에 글자를 직접 써 넣고
    // 같은 버퍼를 다시 쓸 때는 Text가 다시 그리도록 직접 알린다 (문자열 내용 비교로는 바뀐 것을 모르기 때문).
    // 버퍼는 이 클래스 밖으로 내보내지 않는다 (Text 외에는 아무도 이 문자열을 보관하지 않음).
    public sealed class NumberText
    {
        const int MaxDigits = 11; // int 범위 (부호 포함)

        readonly Text text;
        readonly string prefix;
        readonly string suffix;
        readonly string[] buffers = new string[MaxDigits + 1]; // 인덱스 = 숫자 글자 수

        public NumberText(Text text, string prefix, string suffix = "")
        {
            this.text = text;
            this.prefix = prefix;
            this.suffix = suffix;
            for (int d = 1; d <= MaxDigits; d++)
                buffers[d] = new string(' ', prefix.Length + d + suffix.Length); // 처음 1번만 할당

            // 이 크기에서 처음 쓰는 숫자 글자가 웨이브 중에 나오면 폰트 텍스처에 글자를 추가하며 UI 전체를 다시 만든다
            // (작업 10 측정: 넥서스 체력이 처음 줄 때 1회 23KB). 쓸 수 있는 글자를 미리 올려 둔다.
            if (text.font != null)
                text.font.RequestCharactersInTexture("0123456789-" + prefix + suffix, text.fontSize, text.fontStyle);
        }

        public void Set(int value)
        {
            int digits = CountChars(value);
            string buffer = buffers[digits];
            var span = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(buffer.AsSpan()), buffer.Length);

            prefix.AsSpan().CopyTo(span);
            WriteNumber(span.Slice(prefix.Length, digits), value);
            suffix.AsSpan().CopyTo(span.Slice(prefix.Length + digits));

            if (ReferenceEquals(text.text, buffer))
            {
                // 같은 문자열 객체라 Text·TextGenerator가 변경을 알아채지 못하므로 다시 만들게 한다.
                text.cachedTextGenerator.Invalidate();
                text.cachedTextGeneratorForLayout.Invalidate();
                text.SetVerticesDirty();
                text.SetLayoutDirty();
            }
            else
            {
                text.text = buffer;
            }
        }

        static int CountChars(int value)
        {
            if (value == int.MinValue)
                return MaxDigits;
            int n = value < 0 ? 2 : 1;
            uint v = (uint)Math.Abs(value);
            while (v >= 10)
            {
                v /= 10;
                n++;
            }
            return n;
        }

        static void WriteNumber(Span<char> dst, int value)
        {
            long v = value;
            bool negative = v < 0;
            if (negative)
                v = -v;
            for (int i = dst.Length - 1; i >= (negative ? 1 : 0); i--)
            {
                dst[i] = (char)('0' + (int)(v % 10));
                v /= 10;
            }
            if (negative)
                dst[0] = '-';
        }
    }
}
