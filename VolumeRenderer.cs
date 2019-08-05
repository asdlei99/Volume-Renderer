#version 430 core

layout (local_size_x = 16, local_size_y = 16) in;

struct Ray
{
    vec4 origin;
    vec4 dir;
    float length;
    bool is_shadow_ray;
};

struct AABB
{
  vec4 p_min;
  vec4 p_max;
};

const float view_plane_dist = 1.733;
const float EPSILON = 0.000001f;
const float GAMMA = 2.2;
float x_hlen, y_hlen, z_hlen;

AABB bb = AABB(vec4(0,0,0,1), vec4(1,1,1,1));
vec4 step_size;
ivec3 vol_size;

layout(binding = 1, std140) uniform Camera     //    16
{
    mat4 view_mat;                             //    16                 0   (c1)
                                               //    16                 16  (c2)
                                               //    16                 32  (c3)
                                               //    16                 48  (c4)
    vec4 eye;                                  //    16                 64
    float view_plane_dist;                     //    4                  80
} main_cam;

layout(location = 0) uniform vec3 voxel_size;
layout(binding = 0, rgba32f) uniform image2D render_texture;
layout(binding = 1) uniform sampler3D vol_tex3D;

void computeRay(float pixel_x, float pixel_y, int img_width, int img_height, out Ray eye_ray);
bool intersectRayAABB(Ray ray, AABB bb, out float t_min, out float t_max);
vec4 rayMarchVolume(Ray eye_ray, float t_min, float t_max);
vec3 cartesianToTextureCoord(vec4 point);

void main()
{
    ivec2 img_size = imageSize(render_texture);    
    ivec2 pix = ivec2(gl_GlobalInvocationID.xy);
    if (pix.x >= img_size.x || pix.y >= img_size.y)
        return;
    
    vol_size = textureSize(vol_tex3D,0);    
    Ray eye_ray;
    
    //Align bounding box in the center of the screen.
    bb.p_max *= vec4(voxel_size,1.0);
    x_hlen = bb.p_max.x/2.0f;
    y_hlen = bb.p_max.y/2.0f;
    z_hlen = bb.p_max.z/2.0f;
	
    bb.p_min.x -= x_hlen;
    bb.p_max.x -= x_hlen;
    
    bb.p_min.y -= y_hlen;
    bb.p_max.y -= y_hlen;
    
    bb.p_min.z -= z_hlen;
    bb.p_max.z -= z_hlen;
    
    computeRay(pix.x + 0.5, pix.y + 0.5, img_size.x, img_size.y, eye_ray);
    float t_max, t_min;
    
    if(intersectRayAABB(eye_ray, bb, t_min, t_max))
    {
        vec4 color = rayMarchVolume(eye_ray, t_min, t_max);  
        //color.rgb = pow(color.rgb, vec3(1.0/GAMMA));
        //color.rgb = vec3(t_max - t_min);
        imageStore(render_texture, pix, color);
    }
    else
    {
        imageStore(render_texture, pix, vec4(0.4f));
    }
}

vec4 rayMarchVolume(Ray eye_ray, float t_min, float t_max)
{    
    step_size = vec4(voxel_size, 1.0)  /  vec4(vol_size,1.0);    
	//step_size = 1 / vec4(vol_size, 1.0);	
    vec4 start_point = eye_ray.origin + (eye_ray.dir * t_min);
    vec4 end_point = eye_ray.origin + (eye_ray.dir * t_max);        
    
    vec4 dest = vec4(0.0);
    vec4 src = vec4(0.0);    
    vec4 pos = start_point + eye_ray.dir * EPSILON;    
	
    float alpha_scale = 1;	
    vec3 grey = vec3(0.3, 0.3, 0.3);
	
    for(int i = 0; i < 10000; i++)
    {
        vec3 tex_coord = cartesianToTextureCoord(pos);        
        if( any(greaterThan(tex_coord, vec3(1.0))) || any(lessThan(tex_coord, vec3(0.0))) || dest.a >= 0.95)
            break;
        
        src = vec4(texture(vol_tex3D, tex_coord).r);
        if(src.a > 1/255.0)
        {
            src.rgb = grey;
            src.a *= alpha_scale;            
            src *= src.a;
            dest += src * (1 - dest.a);
        }        
        pos += eye_ray.dir * step_size;		
    }
    
    //MIP
    /*for(int i = 0; i < 10000; i++)
    {
        vec3 tex_coord = cartesianToTextureCoord(pos);        
        if(all(greaterThan(tex_coord, vec3(1.0))))
            break;        
        src = vec4(texture(vol_tex3D, tex_coord).r);
        if(dest.r < src.r)
        {
            dest = src;                    
        }
        pos += eye_ray.dir * step_size;
        if(all(greaterThan(pos, end_point)))
            break;
    }*/
    
    return dest;
}

vec3 cartesianToTextureCoord(vec4 point)
{
    //Since the BB was aligned in the center we need to remap the coordinates back in 0-1 range
    point.x += x_hlen;
    point.y += y_hlen;
    point.z += z_hlen;
    
    /* Since (1,1,1) is actually the front side, we need to inverse it so we dont sample from the back
     * when at the start of the bounding box
     */
    point.z = voxel_size.z - point.z;
	//point.z = 1 - point.z;
    return point.xyz/voxel_size;
}

void computeRay(float pixel_x, float pixel_y, int img_width, int img_height, out Ray eye_ray)
{
    float x, y, z, aspect_ratio;
    aspect_ratio = (img_width*1.0)/img_height;
	
	x = aspect_ratio *((2.0 * pixel_x/img_width) - 1);
    y = (2.0 * pixel_y/img_height) -1 ;
    z = -main_cam.view_plane_dist;		
	
	//Orthographic Projection
	/*
	eye_ray.dir = -main_cam.view_mat[2];
	eye_ray.origin = vec4(x,y,z,0);
	eye_ray.origin = main_cam.view_mat * vec4(eye_ray.origin);
	eye_ray.origin.w = 1;	
	eye_ray.length = 99999999.9;*/
	
    eye_ray.dir = normalize(vec4(x,y,z,0));
    eye_ray.dir = normalize((main_cam.view_mat * vec4(eye_ray.dir)));
    eye_ray.origin = main_cam.eye;
    eye_ray.length = 99999999.9;
}

bool intersectRayAABB(Ray ray, AABB bb, out float t_min, out float t_max)
{
    t_max = 1.0/0.0, t_min = -t_max;
    vec3 dir_inv =  1 / ray.dir.xyz;
	
	vec3 min_diff = (bb.p_min - ray.origin).xyz * dir_inv;
    vec3 max_diff = (bb.p_max - ray.origin).xyz * dir_inv;
	
	t_min = max(t_min, min(min_diff.x, max_diff.x));
    t_max = min(t_max, max(min_diff.x, max_diff.x));
	
	t_min = max(t_min, min(min_diff.y, max_diff.y));
    t_max = min(t_max, max(min_diff.y, max_diff.y));
	if(t_max < t_min)
		return false;
	
	t_min = max(t_min, min(min_diff.z, max_diff.z));
    t_max = min(t_max, max(min_diff.z, max_diff.z));
	
    return(t_max > max(t_min, 0));
}
